using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading; // DispatcherOperationCallback
using System.Threading; // Thread
using System.IO;
using System.IO.Ports;
using System.ComponentModel; // CancelEventArgs
using System.Diagnostics; // Debug

namespace DefaultMultiSensor
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private enum RunMode
        {
            Monitor = 0,
            Register = 1,
            Operation = 2,
        };
        private RunMode runMode;

        private enum TeachIn
        {
            _NO = 0,
            _RPS = 1,
            _1BS = 2,
            _4BS = 3,
            _UTE = 4,
            _GP = 6
        };
        private TeachIn teachIn;

        private byte[] readBuffer;
        private bool working = false; // system status for thread control
        private bool stopped = true;  // button status
        Thread timerThread;
        private System.IO.Ports.SerialPort serialPort1;

        /// <summary>
        /// Restrictions for Multi-Sensor
        /// </summary>
        const string sensorEEP = "D2-14-41";
        const int sensorDataLength = 15;
        const int bufferLength = 128;
        /// 

        private uint sensorID;

        private string accelLine;

        private bool filterStatus;
        private bool autoStatus;
        private bool eepRegistered;

        enum RectColor
        {
            L_Lime = 0x01,
            L_White = 0x02,
            R_Red = 0x04,
            R_White = 0x08
        };

        private byte rectangleControl;

        private const string labelTemperatureTitle = "温度";
        private const string labelHumidityTitle = "湿度";
        private const string labelLightenTitle = "照度";
        private const string labelAccelTitle = "加速度";
        private const string labelRadioTitle = "電波強度";
        private const string labelContactTitle = "開閉状態";
        private const string labelAStatusTitle = "加速度状態";

        private readonly int debug = 1;

        public struct EEPData
        {
            public int porg;
            public int func;
            public int type;
            public int manID;
        }

        enum PacketType
        {
            Radio = 0x01,
            Response = 0x02,
            RadioSubTel = 0x03,
            Event = 0x04,
            CommonCommand = 0x05,
            SmartAckCommand = 0x06,
            RmoteManCommand = 0x07,
            RadioMessage = 0x09,
            RadioAdvanced = 0x0A
        };

        private Datafields datafields;

        void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (working)
            {
                if (!stopped)
                {
                    Shutdown();
                    Thread.Sleep(100);
                }
                working = false;
                Thread.Sleep(800);
            }
        }
        public MainWindow()
        {
            InitializeComponent();

            readBuffer = new byte[bufferLength];

            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                ListBoxItem itemNewPort = new ListBoxItem();
                itemNewPort.Content = port;
                itemNewPort.IsSelected = true;
                portSelect.Items.Add(itemNewPort);
                portSelect.ScrollIntoView(itemNewPort);
            }

            datafields = Datafields.Initializer();

            sensorID = 0;
            RectangleClear();
            working = true;

            timerThread = new Thread(TimerThread);
            timerThread.Start();
        }

        private void TimerThread()
        {
            while (working)
            {
                while (!stopped)
                {
                    ReceiveDisplay();
                }
                Thread.Sleep(100); // 100msec
            }
        }

        void BufferFilter(ref byte[] buffer, uint id)
        {
            buffer[0] = 0x55; // Sync Byte
            buffer[1] = 0; // Data Length[0]
            buffer[2] = 7; // Data Length[1]
            buffer[3] = 0; // Optional Length
            buffer[4] = 5; // Packet Type = CO (5)
            buffer[5] = crc.crc8(buffer, 1, 4); // CRC8H
            buffer[6] = 11; // Command Code = CO_WR_FILTER_ADD (11)
            buffer[7] = 0;  // FilterType = Device ID (0)
            buffer[8] = (byte)((id >> 24) & 0xFF); // ID[0]
            buffer[9] = (byte)((id >> 16) & 0xFF); // ID[1]
            buffer[10] = (byte)((id >> 8) & 0xFF); // ID[2]
            buffer[11] = (byte)(id & 0xFF); // ID[3]
            buffer[12] = 0x80; // Filter Kind = apply (0x80)
            buffer[13] = crc.crc8(buffer, 6, 7); // CRC8D
        }

        //
        //
        void SetFilter(bool enable)
        {
            bool clearFilter = true;
            bool writeFilter = enable;
            byte[] writeBuffer = new byte[16];

            if (clearFilter)
            {
                if (true)
                {
                    //Debug.Print("Clear all Filters");
                    writeBuffer[0] = 0x55; // Sync Byte
                    writeBuffer[1] = 0; // Data Length[0]
                    writeBuffer[2] = 1; // Data Length[1]
                    writeBuffer[3] = 0; // Optional Length
                    writeBuffer[4] = 5; // Packet Type = CO (5)
                    writeBuffer[5] = crc.crc8(writeBuffer, 1, 4); // CRC8H
                    writeBuffer[6] = 13; // Command Code = CO_WR_FILTER_DEL (13)
                    writeBuffer[7] = crc.crc8(writeBuffer, 6, 1); // CRC8D
                    serialPort1.Write(writeBuffer, 0, 8);
                    Thread.Sleep(100);
                    //GetResponse();
                }
                if (writeFilter && sensorID != 0)
                {
                    Debug.Print("SwitchID Add Filter");
                    BufferFilter(ref writeBuffer, sensorID);
                    serialPort1.Write(writeBuffer, 0, 14);
                    Thread.Sleep(100);
                }
            }
            if (writeFilter)
            {
                Debug.Print("Enable Filters");
                writeBuffer[0] = 0x55; // Sync Byte
                writeBuffer[1] = 0; // Data Length[0]
                writeBuffer[2] = 3; // Data Length[1]
                writeBuffer[3] = 0; // Optional Length
                writeBuffer[4] = 5; // Packet Type = CO (5)
                writeBuffer[5] = crc.crc8(writeBuffer, 1, 4); // CRC8H
                writeBuffer[6] = 14; // Command Code = CO_WR_FILTER_ENABLE (14)
                writeBuffer[7] = 1;  // Filter Enable = ON (1)
                writeBuffer[8] = 0;  // Filter Operator = OR (0)
                                     //writeBuffer[8] = 1;  // Filter Operator = AND (1)
                writeBuffer[9] = crc.crc8(writeBuffer, 6, 3); // CRC8D

                serialPort1.Write(writeBuffer, 0, 10);
                Thread.Sleep(100);
                //GetResponse();
            }
        }

        public void ReceiveDisplay()
        {
            //uint options;
            //uint inputValue;
            //uint inputValue2;
            int dataLength = 0;
            int dataOffset = 0;
            int optionalLength = 0;
            bool gotHeader = false;
            byte crc8h;
            byte crc8d;
            byte[] data;
            byte[] header;
            byte[] id;
            uint iID;
            int telType = 0; // Telegram type
            int i;
            int nu = 0;
            int readLength;
            int radioStrength;
            bool validTelegram = false;
            PacketType packetType = PacketType.CommonCommand; // just for initialize
            const int headerSize = 5;
            const int leadings = 5; // telType + id[4] = 5

            if (!stopped)
            {
                try
                {
                    readLength = serialPort1.BytesToRead;
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                    Shutdown();
                    return;
                }
                if (readLength == 0)
                {
                    return;
                }

                do
                {
                    header = new byte[headerSize - 1]; // omit crc8h
                    try
                    {
                        do
                        {
                            serialPort1.Read(readBuffer, 0, 1);
                        }
                        while (readBuffer[0] != 0x55);
                        // need catch

                        serialPort1.Read(header, 0, 4);

                        dataLength = header[0] << 8 | header[1];
                        optionalLength = header[2];
                        packetType = (PacketType)header[3];

                        serialPort1.Read(readBuffer, 0, 1);
                        crc8h = readBuffer[0];
                        gotHeader = crc8h == crc.crc8(header);
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine(e.Message);
                        return;
                    }
                }
                while (!gotHeader);

                if (debug > 1)
                    Debug.WriteLine("Got header");

                if (dataLength > bufferLength)
                {
                    dataLength = bufferLength;
                    optionalLength = 0;
                }
                else if (dataLength + optionalLength > bufferLength)
                {
                    optionalLength = bufferLength - dataLength;
                }

                if (dataLength > 0)
                {
                    for (i = 0; i < dataLength; i++) {
                        serialPort1.Read(readBuffer, i, 1);
                    }
                    //serialPort1.Read(readBuffer, 0, dataLength);
                }
                if (optionalLength > 0)
                {
                    for (i = 0; i < optionalLength + 1; i++)
                    {
                        serialPort1.Read(readBuffer, dataLength + i, 1);
                    }
                    //serialPort1.Read(readBuffer, dataLength, optionalLength + 1);
                }

                crc8d = readBuffer[dataLength + optionalLength];
                int crc8dtmp = crc.crc8(readBuffer, dataLength + optionalLength);
                if (crc8d != crc8dtmp)
                {
                    Debug.Print("Invalid data CRC!");
                    return;
                }

                //if (packetType == PacketType.Radio || packetType == PacketType.RadioAdvanced)
                if (packetType == PacketType.RadioAdvanced)
                {
                    // We accept only ERP2 on ESP3
                    data = new byte[bufferLength];
                    id = new byte[4];
                    teachIn = TeachIn._NO;
                    telType = readBuffer[0];
                    radioStrength = readBuffer[dataLength + 1];

                    switch (telType)
                    {
                        case 0x61: //Src 48bit 1BS, Teach-In
                            dataOffset = 2;
                            validTelegram = true;
                            teachIn = TeachIn._1BS;
                            break;
                        case 0x62: //Src 48bit 4BS, Teach-In
                            dataOffset = 2;
                            validTelegram = true;
                            teachIn = TeachIn._4BS;
                            break;
                        case 0x65: //Src 48bit UTE, Teach-In
                            dataOffset = 2;
                            validTelegram = true;
                            teachIn = TeachIn._UTE;
                            break;
                        case 0x20: //Src 32bit RPS, Data
                            nu = (readBuffer[leadings + dataOffset] >> 7) & 0x01;
                            data[0] = (byte)(readBuffer[leadings + dataOffset] & 0x0F);
                            validTelegram = true;
                            teachIn = TeachIn._RPS;
                            break;
                        case 0x21: //Src 32bit 1BS, Data
                            validTelegram = true;
                            break;
                        case 0x22: //Src 32bit 4BS, Data
                            validTelegram = true;
                            break;
                        case 0x24: //Src 32bit VLD, Data
                            validTelegram = true;
                            break;
                        default:
                            Debug.WriteLine($"Unknown telType = {telType:X}");
                            break;
                    }

                    if (!validTelegram)
                    {
                        return;
                    }

                    id[3] = readBuffer[1 + dataOffset];
                    id[2] = readBuffer[2 + dataOffset];
                    id[1] = readBuffer[3 + dataOffset];
                    id[0] = readBuffer[4 + dataOffset];

                    iID = (uint)BitConverter.ToInt32(id, 0);

                    for (i = 0; i < (dataLength - leadings); i++)
                    {
                        data[i] = readBuffer[leadings + dataOffset + i];
                    }
                }
                else
                {
                    //// response have to come
                    ////Debug.WriteLine("Unknown packetType = {0:X}", packetType);
                    return;
                }

                // now chack ID, there ar three modes
                //
                //   registered ID == 0: register/monitor mode for TeachIn
                //     if auto detect: register mode
                //     else; monitor mode
                //   registered ID != 0: operation mode
                //
                if (sensorID != 0)
                {
                    runMode = RunMode.Operation;
                }
                else
                {
                    // Auto Detect ==> Teach In
                    runMode = autoStatus ? RunMode.Register : RunMode.Monitor;
                }

                switch (runMode)
                {
                    case RunMode.Monitor:
                        DisplayTelegram(iID, telType, data);
                        DisplayDbm(radioStrength);
                        break;
                    case RunMode.Register:
                        if (!eepRegistered && teachIn != TeachIn._NO)
                        {
                            DisplayTelegram(iID, telType, data);
                            eepRegistered = TeachInTelegram(iID, telType, data);
                            DisplayDbm(radioStrength);
                        }
                        break;
                    case RunMode.Operation:
                        if (telType == 0x24)
                        {
                            if (dataLength != sensorDataLength)
                            {
                                Debug.WriteLine($"Not supported data length = {dataLength:0}");
                            }
                            else
                            {
                                DisplayVldData(data);
                                DisplayTelegram(iID, telType, data);
                                DisplayDbm(radioStrength);
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"Not supported telType = {telType:X}");
                        }
                        break;
                }
            }
        }

        void DisplayTelegram(uint id, int telType, byte[] data)
        {
            string line;

            line = String.Format("{0:X8} {1:X2} {2:X2} {3:X2} {4:X2} {5:X2} {6:X2} {7:X2}",
                id, telType, data[0], data[1], data[0], data[1], data[0], data[1]);

            Dispatcher.BeginInvoke(
                new DispatcherOperationCallback(TextBox2Text),
                    line + "\r\n");
        }

        void RegisterId(uint id)
        {
            sensorID = id;
            //
            // Display ID
            //
            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(MultiIdText),
                        sensorID.ToString());
        }

        bool TeachInTelegram(uint id, int telType,  byte[] data)
        {
            bool registered = false;
            uint rorg = 0;
            uint func = 0;
            uint type = 0;
            uint manID = 0;
            string strEEP = "";

            switch (telType)
            {
                case 0x61: //Src 48bit 1BS, Teach-In
                    rorg = 0xD5;
                    func = 0x00;
                    type = 0x01;
                    manID = 0x00B;
                    break;
                case 0x62: //Src 48bit 4BS, Teach-In
                    rorg = 0xA5;
                    func = ((uint)data[0]) >> 2;
                    type = ((uint)data[0] & 0x03) << 5 | ((uint)data[1]) >> 3;
                    manID = (uint)(data[1] & 0x07) << 8 | data[2];
                    break;
                case 0x65: //Src 48bit UTE, Teach-In
                    rorg = data[6];
                    func = data[5];
                    type = data[4];
                    manID = (uint)data[3] & 0x07;
                    break;
                case 0x20: //Src 32bit RPS, Data
                    rorg = 0xF6;
                    func = 0x02;
                    type = 0x04;
                    manID = 0x00B;
                    break;
            }
            if (true /* manID != 0 ? change specigication ? */)
            {
                strEEP = String.Format("{0:X2}-{1:X2}-{2:X2}", rorg, func, type);
                if (strEEP == sensorEEP)
                {
                    DisplayEEP((int)rorg, (int)func, (int)type, (int)manID);
                    RegisterId(id);
                    registered = true;
                }
            }
            if (true) //(debug > 2)
                Debug.WriteLine("Teach In {0}: {1:X2}-{2:X2}-{3:X2} {4:X3}",
                    registered ? "OK" : "NG",  rorg, func, type, manID);

            return registered;
        }

        void DisplayVldData(byte[] data)
        {
            int i = 0;
            int partialData = 0;
            int iValue = 0;
            double dValue = 0.0d;
            double slope;
            double offset;
            BitArray ba = new BitArray();

            foreach (Datafield df in datafields)
            {
                Debug.WriteLine("{0}: {1} {2} {3} {4} {5} {6} {7} {8} {9} {10}", i,
                    df.ValueType,
                    df.DataName,
                    df.ShortCut,
                    df.BitOffs,
                    df.BitSize,
                    df.RangeMin,
                    df.RangeMax,
                    df.ScaleMin,
                    df.ScaleMax,
                    df.Unit);

                slope = ba.CalcA(df.RangeMin, df.ScaleMin, df.RangeMax, df.ScaleMax);
                offset = ba.CalcB(df.RangeMin, df.ScaleMin, df.RangeMax, df.ScaleMax);
                partialData = (int) ba.GetBits(data, (uint) df.BitOffs, (uint) df.BitSize);

                switch (df.ValueType)
                {
                    case VALUE_TYPE.VT_Data:
                        dValue = partialData * slope + offset;
                        break;
                    case VALUE_TYPE.VT_Enum:
                    case VALUE_TYPE.VT_Flag:
                        iValue = partialData;
                        break;
                }

                switch (df.ShortCut)
                {
                    case "TP":
                        DisplayTemperature(dValue, df.Unit);
                        break;
                    case "HU":
                        DisplayHumidity(dValue, df.Unit);
                        break;
                    case "IL":
                        DisplayLighten(dValue, df.Unit);
                        break;
                    case "AX":
                    case "AY":
                    case "AZ":
                        DisplayAccel(df.ShortCut, dValue, df.Unit);
                        break;
                    case "AS":
                    case "CO":
                        DisplayRectangle(df.ShortCut, iValue);
                        break;
                }
            }
        }

        void DisplayTemperature(double temp, string unit)
        {
            DateTime now = DateTime.Now;

            string dataText = String.Format("{0:F1}", temp) + unit;
            string timeText = now.ToString("T");

            if (debug > 0)
                Debug.WriteLine(timeText + " " + dataText);

            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(LabelTemperatureText),
                        labelTemperatureTitle + " " + dataText);
        }

        void DisplayHumidity(double hum, string unit)
        {
            DateTime now = DateTime.Now;
            string dataText = String.Format("{0:F1}", hum) + unit;
            string timeText = now.ToString("T");

            if (debug > 0)
                Debug.WriteLine(timeText + " " + dataText);

            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(LabelHumidityText),
                        labelHumidityTitle + " " + dataText);
        }

        void DisplayLighten(double light, string unit)
        {
            DateTime now = DateTime.Now;

            string dataText = String.Format("{0:F1}", light) + unit;
            string timeText = now.ToString("T");

            if (debug > 0)
                Debug.WriteLine(timeText + " " + dataText);

            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(LabelLightenText),
                        labelLightenTitle + " " + dataText);
        }

        void DisplayAccel(string scut, double force, string unit)
        {
            string dataText = String.Format("{0:F1}", force) + unit;

            if (scut == "AX")
            {
                accelLine = "X:" + dataText;
            }
            else if (scut == "AY")
            {
                accelLine += " Y:" + dataText; ;
            }
            else if (scut == "AZ")
            {
                DateTime now = DateTime.Now;
                string timeText = now.ToString("T");
                accelLine += " Z:" + dataText;
                if (debug > 0)
                    Debug.WriteLine(timeText + " " + accelLine);

                Dispatcher.BeginInvoke(
                        new DispatcherOperationCallback(LabelAccelText),
                            labelAccelTitle + " " + accelLine);
            }
        }

        void DisplaySwitch(uint inputValue)
        {
            DateTime now = DateTime.Now;
            string switchMessage = "";
            uint mask = 0x01;
            for (int j = 0; j < 4; j++)
            {
                switch (inputValue & mask)
                {
                    case 0x01:
                        switchMessage = "Left On";
                        break;
                    case 0x02:
                        switchMessage = "Left Off";
                        break;
                    case 0x04:
                        switchMessage = "Right On";
                        break;
                    case 0x08:
                        switchMessage = "Right Off";
                        break;
                } //switch
                mask <<= 1;
            } // for

            string timeText = now.ToString("T");
            if (debug > 0)
                Debug.WriteLine(timeText + " " + switchMessage);

            RectangleSetData(inputValue);
        }

        void DisplayRectangle(string scut, int value)
        {
            if (scut == "AS")
            {
                rectangleControl |= value > 0 ? (byte)RectColor.R_Red : (byte)RectColor.R_White;
            }
            else if (scut == "CO")
            {
                DateTime now = DateTime.Now;
                string contactMessage = "";
                string aStatusMessage = "";
                uint mask = 0x01;
                rectangleControl = value > 0 ? (byte)RectColor.L_Lime : (byte)RectColor.L_White;

                for (int j = 0; j < 4; j++)
                {
                    switch (rectangleControl & mask)
                    {
                        case 0x01:
                            contactMessage = "CO On";
                            break;
                        case 0x02:
                            contactMessage = "CO Off";
                            break;
                        case 0x04:
                            aStatusMessage = "AS On";
                            break;
                        case 0x08:
                            aStatusMessage = "AS Off";
                            break;
                    } //switch
                    mask <<= 1;
                } // for

                string timeText = now.ToString("T");
                if (debug > 0)
                    Debug.WriteLine(timeText + " " + contactMessage + " " + aStatusMessage);

                RectangleSetData(rectangleControl);

                Dispatcher.BeginInvoke(
                        new DispatcherOperationCallback(LabelContactText), contactMessage);
                Dispatcher.BeginInvoke(
                        new DispatcherOperationCallback(LabelAStatusText), aStatusMessage);
            }
        }

        void DisplayDbm(int strength)
        {
            DateTime now = DateTime.Now;

            string dataText = String.Format("-{0} dBm", strength);
            string timeText = now.ToString("T");

            if (debug > 0)
                Debug.WriteLine(timeText + " " + dataText);

            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(LabelRadioText),
                        labelRadioTitle + " " + dataText);
        }

        void DisplayEEP(int rorg, int func, int type, int manID)
        {
            EEPData e;
            e.porg = rorg;
            e.func = func;
            e.type = type;
            e.manID = manID;
            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(labelEEPText), e);
        }

        object LabelTemperatureText(object obj)
        {
            labelTemperature.Content = obj as string;
            return null;
        }

        object LabelHumidityText(object obj)
        {
            labelHumidity.Content = obj as string;
            return null;
        }

        object LabelLightenText(object obj)
        {
            labelLighten.Content = obj as string;
            return null;
        }

        object LabelAccelText(object obj)
        {
            labelAccel.Content = obj as string;
            return null;
        }

        object LabelRadioText(object obj)
        {
            labelRadio.Content = obj as string;
            return null;
        }

        object LabelContactText(object obj)
        {
            labelContact.Content = obj as string;
            return null;
        }

        object LabelAStatusText(object obj)
        {
            labelAStatus.Content = obj as string;
            return null;
        }
        object MultiIdText(object obj)
        {
            multiID.Text = obj as string;
            return null;
        }

        object TextBox2Text(object obj)
        {
            textBox2.Text += obj as string;
            textBox2.ScrollToEnd();
            return null;
        }

        object RectangleDisplay(object obj)
        {
            Nullable<uint> sw = obj as Nullable<uint>;

            if (sw.HasValue)
            {
                uint s = (uint)sw;
                uint mask = 0x01;
                for (int j = 0; j < 4; j++)
                {
                    switch (s & mask)
                    {
                        case 0x01:
                            rectangleContact.Fill = Brushes.Lime;
                            break;
                        case 0x02:
                            rectangleContact.Fill = Brushes.White;
                            break;
                        case 0x04:
                            rectangleAccel.Fill = Brushes.Red;
                            break;
                        case 0x08:
                            rectangleAccel.Fill = Brushes.White;
                            break;
                    } //switch
                    mask <<= 1;
                } // for
            }
            return null;
        }

        void RectangleClear()
        {
            uint u = 0x02 | 0x08;
            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(RectangleDisplay), u);
        }

        void RectangleSetData(uint value)
        {
            uint u = value;
            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(RectangleDisplay), u);
        }

        object labelEEPText(object obj)
        {
            Nullable<EEPData> e = obj as Nullable<EEPData>;

            if (e.HasValue)
            {
                EEPData eep = (EEPData)e;
                string eepText = string.Format("{0:X2}-{1:X2}-{2:X2} {3:X3}",
                    eep.porg, eep.func, eep.type, eep.manID);
                labelEEP.Background = new SolidColorBrush(Colors.MediumBlue);
                labelEEP.Content = eepText;
            }
            else
            {
                labelEEP.Background = new SolidColorBrush(Colors.White);
                labelEEP.Content = "";
            }
            return null;
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            CheckBoxManagement();

            if (stopped)
            {
                textBox2.Text = "";
                RectangleClear();

                try
                {
                    sensorID = Convert.ToUInt32(multiID.Text, 16);
                }
                catch (Exception ex)
                {
                    string s = "sensorID:" + ex.Message;
                    Debug.Print(s);
                    Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text), s + "\r\n");
                }

                if (sensorID != 0)
                {
                    string s = "MultiID:" + sensorID.ToString("X8") + "\r\n";
                    Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text), s);
                }
                else
                {
                    multiID.Text = "0";
                }

                if (portSelect.SelectedIndex >= 0)
                {
                    ListBoxItem portName = (ListBoxItem)portSelect.SelectedValue;

                    textBox1.Text = "Open: " + portName.Content;

                    try
                    {
                        serialPort1 = new SerialPort("COM11", 57600, Parity.None, 8, StopBits.One);
                        serialPort1.PortName = (string)portName.Content;
                        serialPort1.Open();

                        stopped = false;
                        button1.Content = "Stop";
                        labelTemperature.Content =
                            labelHumidity.Content =
                            labelLighten.Content =
                            labelAccel.Content =
                            labelRadio.Content =
                            labelContact.Content =
                            labelAStatus.Content = "";

                        labelEEP.Background = new SolidColorBrush(Colors.White);
                        labelEEP.Content = "";

                        cbFilter.IsEnabled = false;
                        cbAuto.IsEnabled = false;
                    }
                    catch
                    {
                        Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text),
                            "Serial Open Error.\r\n"); // read exception
                        textBox1.Text = "Error! Please Check the COM port.";
                        return;
                    }
                }
                else
                {
                    textBox1.Text = "Error! Please input COM port name.";
                }
                //SetFilter((bool)cbFilter.IsChecked);
                SetFilter(filterStatus);
            }
            else
            {
                stopped = true;
                serialPort1.Close();
                button1.Content = "Start";
                labelTemperature.Content = labelTemperatureTitle;
                labelHumidity.Content = labelHumidityTitle;
                labelLighten.Content = labelLightenTitle;
                labelAccel.Content = labelAccelTitle;
                labelRadio.Content = labelTemperatureTitle;
                labelContact.Content = labelContactTitle;
                labelAStatus.Content = labelAStatusTitle;

                textBox1.Text = "Stop: " + serialPort1.PortName;

                cbFilter.IsEnabled = true;
                cbAuto.IsEnabled = true;
            }
        }

        private void listBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            portSelect.Focus();
        }
        private void CheckBoxManagement()
        {
            filterStatus = (bool)cbFilter.IsChecked;
            autoStatus = (bool)cbAuto.IsChecked;
        }

        private void Shutdown()
        {
            if (!stopped)
            {
                stopped = true;
                try
                {
                    serialPort1.Close();
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }
            }
        }

        private static void DebugWriteLine(string format, string path)
        {
            string s = string.Format(format, path);
            Debug.WriteLine(s);
        }
    }

    /// <summary>
    /// //////////////////////////////////
    /// </summary>

    public enum VALUE_TYPE
    {
        VT_NotUsed = 0,
        VT_Data = 1,
        VT_Flag = 2,
        VT_Enum = 3
    };
    
    public struct Datafield
    {
        public VALUE_TYPE ValueType; // 0: Not used, 1: Data, 2: Binary Flag, 3: Enumerated data
        public string DataName;
        public string ShortCut;
        public int BitOffs;
        public int BitSize;
        public int RangeMin;
        public int RangeMax;
        public float ScaleMin;
        public float ScaleMax;
        public string Unit;
        //Enumtable[] EnumDesc;

        public Datafield(
            VALUE_TYPE ValueType,
            string DataName,
            string ShortCut,
            int BitOffs,
            int BitSize,
            int RangeMin,
            int RangeMax,
            float ScaleMin,
            float ScaleMax,
            string Unit
            //Enumtable[] EnumDesc,
            )
        {
            this.ValueType = ValueType;
            this.DataName = DataName;
            this.ShortCut = ShortCut;
            this.BitOffs = BitOffs;
            this.BitSize = BitSize;
            this.RangeMin = RangeMin;
            this.RangeMax = RangeMax;
            this.ScaleMin = ScaleMin;
            this.ScaleMax = ScaleMax;
            this.Unit = Unit;
            //this.EnumDesc = EnumDesc;
        }
    };
    public class Datafields : List<Datafield>
    {
        public void Add(
            VALUE_TYPE ValueType,
            string DataName,
            string ShortCut,
            int BitOffs,
            int BitSize,
            int RangeMin,
            int RangeMax,
            float ScaleMin,
            float ScaleMax,
            string Unit
            //Enumtable[] EnumDesc
            )
        {
            Add(new Datafield(
                ValueType,
                DataName,
                ShortCut,
                BitOffs,
                BitSize,
                RangeMin,
                RangeMax,
                ScaleMin,
                ScaleMax,
                Unit
                //EnumDesc
                ));
        }

        public static Datafields Initializer()
        {

            Datafields datafields = new Datafields {
                {
                    (VALUE_TYPE)1, //ValueType
                    "Temperature 10", //DataName
                    "TP", //ShortCut
                    0, //Bitoffs
                    10, //Bitsize
                    0, //RangeMin
                    1000, //RangeMax
                    (float) -40, //ScaleMin
                    (float) 60, //ScaleMax
                    "℃" //Unit
                    //{{0, ""}} //Enum
                },
                {
                    (VALUE_TYPE)1,
                    "Humidity",
                    "HU",
                    10, //Bitoffs
                    8, //Bitsize
                    0, //RangeMin
                    200, //RangeMax
                    (float) 0, //ScaleMin
                    (float) 100, //ScaleMax
                    "%" //Unit
                    //{{0, NULL}}, //Enum
                },
                {
                    (VALUE_TYPE)1,
                    "Illumination",
                    "IL",
                    18, //Bitoffs
                    17, //Bitsize
                    0, //RangeMin
                    100000, //RangeMax
                    (float) 0, //ScaleMin
                    (float) 100000, //ScaleMax
                    "lx" //Unit
                    // { { 0, NULL} }, //Enum
                },
                {
                    (VALUE_TYPE)3,
                    "Acceleration Status",
                    "AS",
                    35, //Bitoffs
                    2, //Bitsize
                    0, //RangeMin
                    3, //RangeMax
                    (float) 0, //ScaleMin
                    (float) 3, //ScaleMax
                    "" //Unit
                    // { { 0, "Periodic Update"},{ 1, "Threshold 1 exceeded"}, { 2, "Threshold 2 exceeded"} }, //Enum
                },
                {
                    (VALUE_TYPE)1,
                    "Acceleration X",
                    "AX",
                    37, //Bitoffs
                    10, //Bitsize
                    0, //RangeMin
                    1000, //RangeMax
                    (float) -2.5, //ScaleMin
                    (float) 2.5, //ScaleMax
                    "g" //Unit
                    //{ { 0, NULL} }, //Enum
                },
                {
                    (VALUE_TYPE)1,
                    "Acceleration Y",
                    "AY",
                    47, //Bitoffs
                    10, //Bitsize
                    0, //RangeMin
                    1000, //RangeMax
                    (float) -2.5, //ScaleMin
                    (float) 2.5, //ScaleMax
                    "g" //Unit
                    //{ { 0, NULL} }, //Enum
                },
                {
                    (VALUE_TYPE)1,
                        "Acceleration Z",
                        "AZ",
                        57, //Bitoffs
                        10, //Bitsize
                        0, //RangeMin
                        1000, //RangeMax
                        (float) -2.5, //ScaleMin
                        (float) 2.5, //ScaleMax
                        "g" //Unit
                        //{ { 0, NULL} }, //Enum
                },
                {
                    (VALUE_TYPE)3,
                        "Contact",
                        "CO",
                        67, //Bitoffs
                        1, //Bitsize
                        0, //RangeMin
                        1, //RangeMax
                        (float) 0, //ScaleMin
                        (float) 1, //ScaleMax
                        "" //Unit
                        //{ { 0, "Open"}, { 1, "Close"} }, //Enum
                },
                {
                    (VALUE_TYPE)1,"","",0,0,0,0,(float) 0,(float) 0,"" /*{{0, NULL}}*/
                }
            };

            return datafields;
        }
    }

    public class BitArray
    {
        const int SZ = 8;
        const ulong SEVEN = 7;

        public ulong GetBits(byte[] inArray, uint start, uint length)
        {
            ulong ul = 0;
            uint startBit = (uint)start % SZ;
            uint startByte = (uint)start / SZ;
            //BYTE* pb = &inArray[startByte];
            uint posInArray = startByte;
            ulong dataInArray;
            uint i;
            uint pos;

            pos = startBit;
            for (i = 0; i < length; i++)
            {
                ul <<= 1;
                dataInArray = inArray[posInArray];
                ul |= (dataInArray >> (int)(SEVEN - pos++)) & 1;
                if (pos >= SZ)
                {
                    pos = 0;
                    dataInArray++;
                }
            }
            return ul;
        }

        public double CalcA(double x1, double y1, double x2, double y2)
        {
            return (double)(y1 - y2) / (double)(x1 - x2);
        }

        public double CalcB(double x1, double y1, double x2, double y2)
        {
            return (((double)x1 * y2 - (double)x2 * y1) / (double)(x1 - x2));
        }
    }

    public static class crc
    {
        private static Byte[] crc8Table = new Byte[]
        {
                0x00, 0x07, 0x0e, 0x09, 0x1c, 0x1b, 0x12, 0x15,
                0x38, 0x3f, 0x36, 0x31, 0x24, 0x23, 0x2a, 0x2d,
                0x70, 0x77, 0x7e, 0x79, 0x6c, 0x6b, 0x62, 0x65,
                0x48, 0x4f, 0x46, 0x41, 0x54, 0x53, 0x5a, 0x5d,
                0xe0, 0xe7, 0xee, 0xe9, 0xfc, 0xfb, 0xf2, 0xf5,
                0xd8, 0xdf, 0xd6, 0xd1, 0xc4, 0xc3, 0xca, 0xcd,
                0x90, 0x97, 0x9e, 0x99, 0x8c, 0x8b, 0x82, 0x85,
                0xa8, 0xaf, 0xa6, 0xa1, 0xb4, 0xb3, 0xba, 0xbd,
                0xc7, 0xc0, 0xc9, 0xce, 0xdb, 0xdc, 0xd5, 0xd2,
                0xff, 0xf8, 0xf1, 0xf6, 0xe3, 0xe4, 0xed, 0xea,
                0xb7, 0xb0, 0xb9, 0xbe, 0xab, 0xac, 0xa5, 0xa2,
                0x8f, 0x88, 0x81, 0x86, 0x93, 0x94, 0x9d, 0x9a,
                0x27, 0x20, 0x29, 0x2e, 0x3b, 0x3c, 0x35, 0x32,
                0x1f, 0x18, 0x11, 0x16, 0x03, 0x04, 0x0d, 0x0a,
                0x57, 0x50, 0x59, 0x5e, 0x4b, 0x4c, 0x45, 0x42,
                0x6f, 0x68, 0x61, 0x66, 0x73, 0x74, 0x7d, 0x7a,
                0x89, 0x8e, 0x87, 0x80, 0x95, 0x92, 0x9b, 0x9c,
                0xb1, 0xb6, 0xbf, 0xb8, 0xad, 0xaa, 0xa3, 0xa4,
                0xf9, 0xfe, 0xf7, 0xf0, 0xe5, 0xe2, 0xeb, 0xec,
                0xc1, 0xc6, 0xcf, 0xc8, 0xdd, 0xda, 0xd3, 0xd4,
                0x69, 0x6e, 0x67, 0x60, 0x75, 0x72, 0x7b, 0x7c,
                0x51, 0x56, 0x5f, 0x58, 0x4d, 0x4a, 0x43, 0x44,
                0x19, 0x1e, 0x17, 0x10, 0x05, 0x02, 0x0b, 0x0c,
                0x21, 0x26, 0x2f, 0x28, 0x3d, 0x3a, 0x33, 0x34,
                0x4e, 0x49, 0x40, 0x47, 0x52, 0x55, 0x5c, 0x5b,
                0x76, 0x71, 0x78, 0x7f, 0x6A, 0x6d, 0x64, 0x63,
                0x3e, 0x39, 0x30, 0x37, 0x22, 0x25, 0x2c, 0x2b,
                0x06, 0x01, 0x08, 0x0f, 0x1a, 0x1d, 0x14, 0x13,
                0xae, 0xa9, 0xa0, 0xa7, 0xb2, 0xb5, 0xbc, 0xbb,
                0x96, 0x91, 0x98, 0x9f, 0x8a, 0x8D, 0x84, 0x83,
                0xde, 0xd9, 0xd0, 0xd7, 0xc2, 0xc5, 0xcc, 0xcb,
                0xe6, 0xe1, 0xe8, 0xef, 0xfa, 0xfd, 0xf4, 0xf3
        };

        public static Byte crc8(Byte[] data, int offset, int count)
        {
            Byte crc = 0;
            count += offset;
            for (int i = offset; i < count; i++)
                crc = crc8Table[crc ^ data[i]];
            return crc;
        }

        public static Byte crc8(Byte[] data, int count)
        {
            return crc8(data, 0, count);
        }

        public static Byte crc8(Byte[] data)
        {
            return crc8(data, data.Length);
        }
    }
}