using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

namespace EnOceanSample
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private byte[] readBuffer;
        private bool gotHeader = false;
        private bool working = false; // system status for thread control
        private bool stopped = true;  // button status
        Thread timerThread;

        private byte[] data;
        private byte[] id;
        private System.IO.Ports.SerialPort serialPort1;

        private uint switchID;
        private uint tempID;
        private readonly double temperatureOffset = 40.0D;
        private readonly double temperatureSlope = -40.0D / 255.0D;

        private const string labelTemperatureTitle = "温度";
        private const string labelSwitchTitle = "スイッチ";
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

            readBuffer = new byte[64];
            data = new byte[4];
            id = new byte[4];

            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                ListBoxItem itemNewPort = new ListBoxItem();
                itemNewPort.Content = port;
                itemNewPort.IsSelected = true;
                portSelect.Items.Add(itemNewPort);
                portSelect.ScrollIntoView(itemNewPort);
            }

            switchID = tempID = 0;
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

        private double TemperatureGauge(uint u)
        {
            double temperature = temperatureSlope * u + temperatureOffset;
            return temperature;
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
                if (writeFilter && switchID != 0)
                {
                    Debug.Print("SwitchID Add Filter");
                    BufferFilter(ref writeBuffer, switchID);
                    serialPort1.Write(writeBuffer, 0, 14);
                    Thread.Sleep(100);
                }
                if (writeFilter && tempID != 0)
                {
                    Debug.Print("TempID Add Filter");
                    BufferFilter(ref writeBuffer, tempID);
                    serialPort1.Write(writeBuffer, 0, 14);
                    Thread.Sleep(100);
                    //GetResponse();
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
            uint options;
            uint inputValue;
            uint inputValue2;
            int dataLength = 0;
            int dataOffset = 0;
            byte optionalLength = 0;
            PacketType packetType = PacketType.CommonCommand;
            byte crc8h;
            byte crc8d;
            byte[] header = new byte[4];
            gotHeader = false;
            int rorg = 0;
            int[] data = new int[4];
            int nu = 0;

            if (!stopped)
            {
                int readLength;
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

                for (int i = 0; i < dataLength; i++)
                {
                    serialPort1.Read(readBuffer, i, 1);
                }

                if (packetType == PacketType.Radio || packetType == PacketType.RadioAdvanced)
                {
                    rorg = readBuffer[0];
                    if (rorg == 0x62)
                    {
                        dataOffset = 2;
                    }
                    id[3] = readBuffer[1 + dataOffset];
                    id[2] = readBuffer[2 + dataOffset];
                    id[1] = readBuffer[3 + dataOffset];
                    id[0] = readBuffer[4 + dataOffset];

                    if (rorg == 0x20) // RPS
                    {
                        //dataSize = 1;
                        nu = (readBuffer[5 + dataOffset] >> 7) & 0x01;
                        data[0] = readBuffer[5 + dataOffset] & 0x0F;
                        data[1] = 0;
                        data[2] = 0;
                        data[3] = 0;
                    }
                    else if (rorg == 0x22) // 4BS
                    {
                        //dataSize = 4;
                        data[0] = readBuffer[5 + dataOffset];
                        data[1] = readBuffer[6 + dataOffset];
                        data[2] = readBuffer[7 + dataOffset];
                        data[3] = readBuffer[8 + dataOffset];
                    }
                    else if (rorg == 0x62)  // Teach-In
                    {
                        //dataSize = 4;
                        data[0] = readBuffer[5 + dataOffset];
                        data[1] = readBuffer[6 + dataOffset];
                        data[2] = readBuffer[7 + dataOffset];
                        data[3] = readBuffer[8 + dataOffset];
                    }
                    else
                    {
                        Debug.WriteLine("Unknown rorg = {0}", rorg);
                    }
                }
                if (optionalLength > 0)
                {
                    serialPort1.Read(readBuffer, dataLength, optionalLength);
                }

                serialPort1.Read(readBuffer, dataLength + optionalLength, 1);
                crc8d = readBuffer[dataLength + optionalLength];

                if (crc8d != crc.crc8(readBuffer, dataLength + optionalLength))
                {
                    Debug.Print("Invalid data CRC!");
                    return;
                }

                //////////////////////////////////////
                ///         Break here !            // 
                //////////////////////////////////////
                int iID = BitConverter.ToInt32(id, 0);
                if (iID != 0)
                {
                    options = (uint)data[3]; // Read Input Value from data[3] //
                    inputValue = (uint)data[2]; // Read Input Value from data[2] //
                    inputValue2 = (uint)data[1]; // Read Input Value from data[1] //

                    if (debug > 0)
                    {
                        string IdData = "ID: ";
                        IdData += iID.ToString("X8");

                        for (int i = 0; i < 4; i++)
                        {
                            IdData += " " + data[i].ToString("X2");
                        }
                        Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text), IdData + "\r\n");
                        Debug.Print(IdData);
                    }

                    if (rorg == 0x62) // Teach-In
                    {
                        // get EEP, Teach In Telegram
                        uint func = ((uint)data[0]) >> 2;
                        uint type = ((uint)data[0] & 0x03) << 5 | ((uint)data[1]) >> 3;
                        int manID = (data[1] & 0x07) << 8 | data[2];

                        if (true) //(debug > 2)
                            Debug.WriteLine("Teach In: A5-{0:X2}-{1:X2} {2:X3}", func, type, manID);
                        if (manID != 0)
                            DisplayEEP((int)func, (int)type, manID);
                    }
                    else if (rorg == 0x22)
                    {
                        if (tempID == 0 || tempID == iID)
                        {
                            DisplayTemperature(inputValue);
                        }
                    }
                    else if (rorg == 0x20 && nu == 0x01)
                    {
                        if (switchID == 0 || switchID == iID)
                        {
                            DisplaySwitch((uint)data[0]);
                        }
                    }
                }
                else
                {
                    if (debug > 2)
                        Debug.WriteLine("Zero ID");
                }
            }
        }

        void DisplayTemperature(uint inputValue)
        {
            DateTime now = DateTime.Now;

            double temp = TemperatureGauge(inputValue);

            string temperatureText = String.Format("{0:F1}", temp) + " ℃";
            string timeText = now.ToString("T");

            if (debug > 0)
                Debug.WriteLine(timeText + " " + temperatureText);

            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(LabelTemperatureText),
                        labelTemperatureTitle + " " + temperatureText);
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
            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(LabelSwitchText),
                        switchMessage);
            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(RectangleDisplay),
                        inputValue);
        }

        void DisplayEEP(int func, int type, int manID)
        {
            EEPData e;
            e.porg = 0xA5;
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

        object LabelSwitchText(object obj)
        {
            labelSwitch.Content = obj as string;
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
                            rectangleLeft.Fill = Brushes.Lime;
                            break;
                        case 0x02:
                            rectangleLeft.Fill = Brushes.White;
                            break;
                        case 0x04:
                            rectangleRight.Fill = Brushes.Red;
                            break;
                        case 0x08:
                            rectangleRight.Fill = Brushes.White;
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
            if (stopped)
            {
                textBox2.Text = "";
                RectangleClear();

                try
                {
                    switchID = Convert.ToUInt32(SwitchID.Text, 16);
                }
                catch (Exception ex)
                {
                    string s = "SwitchID:" + ex.Message;
                    Debug.Print(s);
                    Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text), s + "\r\n");
                }
                try
                {
                    tempID = Convert.ToUInt32(TempID.Text, 16);
                }
                catch (Exception ex)
                {
                    string s = "TempID:" + ex.Message;
                    Debug.Print(s);
                    Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text), s + "\r\n");
                }

                if (switchID != 0)
                {
                    string s = "SW:" + switchID.ToString("X8") + "\r\n";
                    Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text), s);
                }
                else
                {
                    SwitchID.Text = "0";
                }
                if (tempID != 0)
                {
                    string s = "Temp:" + tempID.ToString("X8") + "\r\n";
                    Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text), s);
                }
                else
                {
                    TempID.Text = "0";
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
                        labelTemperature.Content = "";

                        labelEEP.Background = new SolidColorBrush(Colors.White);
                        labelEEP.Content = "";
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
                SetFilter((bool)cbFilter.IsChecked);
            }
            else
            {
                stopped = true;
                serialPort1.Close();
                button1.Content = "Start";
                labelTemperature.Content = labelTemperatureTitle;
                labelSwitch.Content = labelSwitchTitle;
                textBox1.Text = "Stop: " + serialPort1.PortName;
            }
        }

        private void listBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            portSelect.Focus();
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
