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
using System.Windows.Threading;
using System.Threading;
using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Diagnostics;
using Microsoft.Win32;

namespace CO2Sensor
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private byte[] readBuffer;
        public bool working = false; // system status for thread control
        public bool stopped = true;  // button status
        private static Semaphore onDisplay; // Dislay control
        Thread timerThread;
        Thread displaySupportThread;

        private byte[] data;
        private byte[] id;

        private byte[] regData;

        private DateTime lastDt;
        private DateTime currentDt;

        private System.IO.Ports.SerialPort serialPort;

        private const double defaultCO2Calibration = 0.0;
        private const double defaultCO2Inclination = 10.0;

        private const double defaultHumidCalibration = 0.0;
        private const double defaultHumidInclination = 0.5;

        private const double defaultTempCalibration = 0.0;
        private const double defaultTempInclination = 0.2;

        private const string labelCO2Title = "CO2 Sensor";
        private const string labelCO2SubTitle = "CO2";

        private int co2ID;
        private double co2Calibration = 0D;
        private double co2Inclination = 0D;

        private double humidCalibration = defaultHumidCalibration;
        private double humidInclination = defaultHumidInclination;

        private double tempCalibration = defaultTempCalibration;
        private double tempInclination = defaultTempInclination;

        //private readonly int defaultCO2ID = 0x04100178;
        //private readonly int defaultCO2ID = 0x04100163;
        private readonly int defaultCO2ID = 0x040184A8;

        private readonly uint[] cIDs = {
                             //0x04100169,
                               0x04100178,
                               0x04100163}; // CO2 Sensor node ID

        private const int maxCO2s = 128;
        private double[] co2s = new double[maxCO2s];
        private int co2Index = 0;

        private const int maxHumids = 128;
        private double[] humids = new double[maxHumids];
        private int humidIndex = 0;

        private const int maxTemps = 255;
        private double[] temps = new double[maxHumids];
        private int tempIndex = 0;

        private const string configDir = @"\DEVDRV\CO2 Sensor\";

        private readonly int debug = 1; // 1; //0; // //1; ////////
        private readonly bool noRegistory = true;

        public struct CO2Data
        {
            public DateTime Timestamp;
            public double CO2; // CO2 ppm
            public double Humid; // Level percent
            public double Temp; // Temp Celsius
            public bool Redraw;
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

        public struct EEPData
        {
            public byte porg;
            public byte func;
            public byte type;
            public ushort manID;
            public EEPData(byte p, byte f, byte t, ushort m)
            {
                porg = p;
                func = f;
                type = t;
                manID = m;
            }
        };

        public MainWindow()
        {
            InitializeComponent();

            readBuffer = new byte[16];
            data = new byte[4];
            id = new byte[4];
            regData = new byte[8]; // registory
            onDisplay = new Semaphore(1, 1);

            string[] ports = SerialPort.GetPortNames();
            //StringComparer cmp = StringComparer.OrdinalIgnoreCase;
            //Array.Sort(ports, cmp);
            Array.Sort(ports);
            foreach (string port in ports)
            {
                ListBoxItem itemNewPort = new ListBoxItem();
                itemNewPort.Content = port;
                itemNewPort.IsSelected = true;
                portSelect.Items.Add(itemNewPort);
                portSelect.ScrollIntoView(itemNewPort);
            }

            currentDt = lastDt = DateTime.MinValue;
            co2ID = 0;
            for (int i = 0; i < maxCO2s; i++)
            {
                co2s[i] = 0.0D;
            }

            readRegistory(); ////////
            gpX.Initialize();
            working = true;

            displaySupportThread = new Thread(DisplaySupportThread);
            displaySupportThread.Start();
            timerThread = new Thread(TimerThread);
            timerThread.Start();
        }

        private void TimerThread()
        {
            while (working)
            {
                lastDt = DateTime.MinValue;
                while (!stopped)
                {
                    ReceiveDisplay();
                }
                Thread.Sleep(300); // 300msec
            }
        }

        private void DisplaySupportThread()
        {
            while (working)
            {
                while (!stopped)
                {
                    Thread.Sleep(9973); // ~ 10 sec

                    RedrawCO2(); //
                }
                Thread.Sleep(3333); // 3333msec
            }
        }

        private double CO2Gauge(uint u)
        {
            double co2 = co2Inclination * u + co2Calibration;

            co2s[co2Index] = co2;
            co2Index = (co2Index + 1) & (maxCO2s - 1);

            if (debug > 0)
                Debug.WriteLine("added index:" + co2Index + "co2:" + co2);

            return co2;
        }

        private double HumidGauge(uint u)
        {
            double humid = humidInclination * u + humidCalibration;

            humids[humidIndex] = humid;
            humidIndex = (humidIndex + 1) & (maxHumids - 1);

            if (debug > 0)
                Debug.WriteLine("added index:" + humidIndex + "humid:" + humid);

            return humid;
        }

        private double TempGauge(uint u)
        {
            double temp = tempInclination * u + tempCalibration;

            temps[tempIndex] = temp;
            tempIndex = (tempIndex + 1) & (maxTemps - 1);

            if (debug > 0)
                Debug.WriteLine("added index:" + tempIndex + "temp:" + temp);

            return temp;
        }

        public void ReceiveDisplay()
        {
            uint inputValue;
            uint inputValue2;
            uint inputValue3;
            uint inputChoice;
            int bytesToRead = 0;
            int dataLength = 0;
            int dataOffset = 0;
            byte optionalLength = 0;
            PacketType packetType = PacketType.CommonCommand;
            byte crc8h;
            byte crc8d;
            byte[] header = new byte[4];
            bool gotHeader = false;
            int rorg = 0;
            //int[] data = new int[4];
            byte[] data = new byte[4];
            int nu = 0;
            byte[] readBuffer = new byte[40];
            byte[] writeBuffer = new byte[16];
            byte[] id = new byte[4];
            uint iID;

            if (stopped)
            {
                return;
            }
            try
            {
                bytesToRead = serialPort.BytesToRead;
            }
            catch (Exception)
            {
                Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(TextBox2Text),
                        "Check USB Cable.\r\n"); // read exception
                stopped = true;
                return;
            }
            if (bytesToRead > 0)
            {
                do
                {
                    do
                    {
                        if (stopped) // maybe window is closing
                        {
                            return;
                        }
                        try
                        {
                            serialPort.Read(readBuffer, 0, 1);
                        }
                        catch (Exception)
                        {
                            Dispatcher.BeginInvoke(
                                new DispatcherOperationCallback(TextBox2Text),
                                    "Stopped.\r\n"); // read exception
                            stopped = true;
                            return;
                        }
                    }
                    while (readBuffer[0] != 0x55);

                    try
                    {
                        serialPort.Read(header, 0, 4);

                        dataLength = header[0] << 8 | header[1];
                        optionalLength = header[2];
                        packetType = (PacketType)header[3];

                        serialPort.Read(readBuffer, 0, 1);
                        crc8h = readBuffer[0];

                        gotHeader = crc8h == crc.crc8(header);
                    }
                    catch (Exception)
                    {
                        Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text),
                                "Stopped.\r\n"); // read exception
                        stopped = true;
                        return;
                    }
                }
                while (!gotHeader);

                if (debug > 1)
                    Debug.WriteLine("Got header");

                if (dataLength > 40)
                    dataLength = 40;

                for (int i = 0; i < readBuffer.Length; i++)
                {
                    // clear buffer for debug
                    readBuffer[i] = 0;
                }
                for (int i = 0; i < dataLength; i++)
                {
                    serialPort.Read(readBuffer, i, 1);
                }

                if (packetType == PacketType.Radio || packetType == PacketType.RadioAdvanced)
                {
                    rorg = readBuffer[0];
                    dataOffset = rorg == 0x62 ? 2 : 0;

                    id[3] = readBuffer[1 + dataOffset];
                    id[2] = readBuffer[2 + dataOffset];
                    id[1] = readBuffer[3 + dataOffset];
                    id[0] = readBuffer[4 + dataOffset];

                    if (rorg == 0x20) // RPS
                    {
                        //dataSize = 1;
                        nu = (readBuffer[5 + dataOffset] >> 7) & 0x01;
                        data[0] = (byte)(readBuffer[5 + dataOffset] & 0x0F);
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
                        //Debug.Print("Unknown rorg = " + rorg);
                    }
                }
                else if (packetType == PacketType.Response)
                {
                    Debug.Print("dataLength:" + dataLength + " RES:" + readBuffer[0]);

                    data[0] = readBuffer[0]; // Return Code
                    data[1] = readBuffer[1]; //APP Version[0]
                    data[2] = readBuffer[2]; //APP Version[1]
                    data[3] = readBuffer[3]; //APP Version[2]

                    return;
                }
                else
                {
                    // We ignore other type.
                    //Debug.Print("We ignore other type:" + packetType);

                    return;
                }

                if (optionalLength > 0)
                {
                    serialPort.Read(readBuffer, dataLength, optionalLength);
                }

                serialPort.Read(readBuffer, dataLength + optionalLength, 1);
                crc8d = readBuffer[dataLength + optionalLength];

                if (crc8d != crc.crc8(readBuffer, dataLength + optionalLength))
                {
                    return;
                }

                // Display ID
                //string IdText = "ID: ";
                //for (int i = 0; i < 4; i++)
                //{
                //    IdText += id[i].ToString("X2");
                //}
                //IdText += "\r\n";

                // Display Data
                if (debug > 1)
                {
                    string IdData = "Data: ";
                    for (int i = 0; i < 4; i++)
                    {
                        IdData += data[i].ToString("X2");
                    }
                    Debug.WriteLine(IdData);
                }
                //more,...
                //int iID = BitConverter.ToInt32(id, 0);
                iID = (uint)((id[0])
                        | (id[1] << 8)
                        | (id[2] << 16)
                        | (id[3] << 24));

                if (iID != 0)
                {
                    inputValue = (uint)data[0];  // Humidity as Voltage: data[0] //
                    inputValue2 = (uint)data[1]; // Concentration: data[1] //
                    inputValue3 = (uint)data[2]; // Tempareture: data[2] //
                    inputChoice = (uint)data[3]; // Not used Choise: data[3] //

                    if (debug > 1)
                    {
                        uint iV = inputValue2; // CO2 Concentration
                        string IdText = "ID: ";
                        IdText += iID.ToString("X8");
                        if (debug > 1)
                        {
                            string s = IdText + " " + inputValue.ToString("X2")
                                + " " + inputValue2.ToString("X2") + "\r\n";
                            Debug.Print(s);
                        }

                        IdText += " " + iV.ToString("X2") + "\r\n";

                        Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text), IdText);
                    }
                    //
                    // Check and Dispatch by ID and rorg
                    //
                    if (rorg == 0x62 && iID == co2ID) // Teach-In
                    {
                        // get EEP, Teach In Telegram
                        uint func = ((uint)data[0]) >> 2;
                        uint type = ((uint)data[0] & 0x03) << 5 | ((uint)data[1]) >> 3;
                        int manID = (data[1] & 0x07) << 8 | data[2];

                        if (debug > 2)
                            Debug.Print("Teach In: A5-" +
                                func.ToString("X2") + "-" +
                                type.ToString("X2") + " " +
                                manID.ToString("X3"));
                        if (manID != 0)
                        {
                            //DisplayEEP((int)func, (int)type, manID);
                        }
                    }
                    //else if (rorg == 0x22 && Array.IndexOf(cIDs, iID) >= 0)
                    else if (rorg == 0x22 && iID == co2ID)
                    {
                        if (debug > 2)
                            Debug.Print("4BS CO2");

                        int value = (int)(inputValue2) * 10;
                        string co2 = value.ToString() + "ppm ";
                        value = (int)(inputValue) / 2;
                        co2 += value.ToString() + "% ";
                        value = (int)(inputValue3) * 5;
                        co2 += value.ToString() + "℃";

                        Debug.Print(co2);
                        DisplayCO2(inputValue2, inputValue, inputValue3);

                    }
                    else if (rorg == 0x20) // RPS
                    {
                        if (debug > 3)
                            Debug.Print("RPS type");
                    }
                    else
                    {
                        if (debug > 3)
                            Debug.Print("Unknown type");
                    }
                }
                else
                {
                    if (debug > 2)
                        Debug.WriteLine("Zero ID");
                }
            }
        }

        void DisplayCO2(uint inputCo2, uint inputHumid, uint inputTemp)
        {
            DateTime now = DateTime.Now;
            TimeSpan ts = now - currentDt;
            int msec = 1000 * (ts.Hours * 3600 + ts.Minutes * 60 + ts.Seconds) + ts.Milliseconds;
            currentDt = now;

            if (debug > 0)
                Debug.WriteLine(ts.ToString() + " " + inputCo2.ToString() + " " + msec.ToString());

            //if (msec < 750) ////
            //{
            //    if (debug > 0)
            //        Debug.WriteLine("skip = " + msec.ToString());
            //    return;
            //}

            onDisplay.WaitOne();
            double co2 = CO2Gauge(inputCo2);
            double humid = HumidGauge(inputHumid);
            double temp = TempGauge(inputTemp);

            string co2Text = String.Format("{0} ppm", co2);
            string timeText = now.ToString("T");

            if (debug > 0)
                Debug.WriteLine(timeText + " " + co2Text);

            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(LabelCO2Text),
                        labelCO2SubTitle + " " + co2Text); ////

            CO2Data t;
            t.Timestamp = now;
            t.CO2 = co2;
            t.Humid = humid;
            t.Temp = temp;
            t.Redraw = false;

            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(CO2TrendGraph),
                    t);

            onDisplay.Release();
        }

        void RedrawCO2()
        {
            DateTime now = DateTime.Now;
            TimeSpan ts = now - currentDt;
            int msec = 1000 * (ts.Hours * 3600 + ts.Minutes * 60 + ts.Seconds) + ts.Milliseconds;
            currentDt = now;

            if (debug > 0)
                Debug.WriteLine(ts.ToString() + "Redraw:" + msec.ToString());

            if (msec < 750)
            {
                if (debug > 0)
                    Debug.WriteLine("skip near time = " + msec.ToString());
                return;
            }

            if (onDisplay.WaitOne(0, false) == false)
            {
                if (debug > 0)
                    Debug.WriteLine("skip semaphore = " + msec.ToString());
                return;
            }

            CO2Data co2;
            co2.Timestamp = now;
            co2.CO2 = -999;
            co2.Humid = -999;
            co2.Temp = -999;
            co2.Redraw = true;

            Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(CO2TrendGraph),
                    co2);
            ;

            onDisplay.Release();
        }

        object LabelCO2Text(object obj)
        {
            labelCO2.Content = obj as string;
            return null;
        }

        //object labelHexText(object obj)
        //{
        //    labelHex.Content = obj as string;
        //    return null;
        //}

        object TextBox2Text(object obj)
        {
            textBox2.Text += obj as string;
            textBox2.ScrollToEnd();
            return null;
        }

        object CO2TrendGraph(object obj)
        {
            Nullable<CO2Data> td = obj as Nullable<CO2Data>;

            if (td.HasValue)
            {
                CO2Data t = (CO2Data)td;

                if (t.Redraw)
                {
                    gpX.DrawGraph();
                    return null;
                }

                //tbX.Text = String.Format("{0:0.000}", t.);
                gpX.AddCo2Point(t.Timestamp, t.CO2);
                gpX.AddHumidPoint(t.Timestamp, t.Humid);
                gpX.AddTempPoint(t.Timestamp, t.Temp);

                String logLine = t.Timestamp.ToString("T") + " " +
                    String.Format("{0}", t.CO2) + "ppm";

                if (true)
                {
                    logLine += " " + String.Format("{0}", t.Humid) + "%";
                    logLine += " " + String.Format("{0}", t.Temp) + "℃";
                }
                logLine += "\r\n";
                textBox2.Text += logLine;

                //textBox2.Select(textBox2.Text.Length, 0);
                textBox2.ScrollToEnd();

                gpX.DrawGraph();
            }
            return null;
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            if (stopped)
            {
                textBox2.Text = "";
                if (portSelect.SelectedIndex >= 0)
                {
                    ListBoxItem portName = (ListBoxItem)portSelect.SelectedValue;

                    textBox1.Text = "Open: " + portName.Content;

                    try
                    {
                        serialPort = new SerialPort("COM3", 57600, Parity.None, 8, StopBits.One);
                        serialPort.PortName = (string)portName.Content;
                        serialPort.Open();

                        stopped = false;
                        button1.Content = "Stop";
                        labelCO2.Content = "";
                    }
                    catch
                    {
                        Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(TextBox2Text),
                            "Serial Open Error.\r\n"); // read exception
                        textBox1.Text = "Error! Please Check the COM port.";
                        return;
                    }
                    textBox1.Text = "Start: " + serialPort.PortName;
                }
                else
                {
                    textBox1.Text = "Error! Please input COM port name.";
                }
            }
            else
            {
                stopped = true;
                serialPort.Close();
                button1.Content = "Start";
                labelCO2.Content = labelCO2Title;
                currentDt = DateTime.MinValue;
                gpX.Clear();
                textBox1.Text = "Stop: " + serialPort.PortName;
            }
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            if (!gpX.ShowHumidLevel)
            {
                gpX.ShowHumidLevel = true;
                button2.FontWeight = FontWeights.Bold;
            }
            else
            {
                gpX.ShowHumidLevel = false;
                button2.FontWeight = FontWeights.Regular;
            }
            RedrawCO2();
        }

        private void listBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            portSelect.Focus();
        }

        private void textBox1_TextChanged(object sender, TextChangedEventArgs e)
        {
            ;
        }

        private void textBox2_TextChanged(object sender, TextChangedEventArgs e)
        {
            ;
        }

        private Object GetObjectFromName(string fieldname)
        {
            Object o = null;
            FieldInfo fi = this.GetType().GetField(fieldname,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.DeclaredOnly);

            if (fi != null)
            {
                o = fi.GetValue(this);
            }
            return o;
        }

        private void readRegistory()
        {
            string s;
            byte[] b;

            if (!noRegistory)
            {
                s = getRegistory("CO2Node");
                if (String.IsNullOrEmpty(s))
                {
                    co2ID = 0;
                }
                else
                {
                    b = Encoding.ASCII.GetBytes(s);
                    try
                    {
                        co2ID = Convert.ToInt32(s, 16);
                    }
                    catch
                    {
                        co2ID = 0;
                    }
                    s = "Reg CO2: " + s;
                    TextBox2Text(s + "\r\n");
                }

                s = getRegistory("CO2Calibration");
                if (String.IsNullOrEmpty(s))
                {
                    co2Calibration = 0D;
                }
                else
                {
                    b = Encoding.ASCII.GetBytes(s);
                    try
                    {
                        co2Calibration = Convert.ToDouble(s);
                    }
                    catch
                    {
                        co2Calibration = 0D;
                    }
                    s = "Reg Calibration: " + s;
                    TextBox2Text(s + "\r\n");
                }

                s = getRegistory("CO2Inclination");
                if (String.IsNullOrEmpty(s))
                {
                    co2Inclination = 0D;
                }
                else
                {
                    b = Encoding.ASCII.GetBytes(s);
                    try
                    {
                        co2Inclination = Convert.ToDouble(s);
                    }
                    catch
                    {
                        co2Inclination = 0D;
                    }
                    s = "Reg Inclination: " + s;
                    TextBox2Text(s + "\r\n");
                }
            } // noRegistroy

            /////////////////////////////////////

            if (co2ID == 0)
            {
                s = getFile("CO2Node");
                if (String.IsNullOrEmpty(s))
                {
                    string mes = "TID notfound: use default=" + defaultCO2ID.ToString("X8");
                    TextBox2Text(mes + "\r\n");
                    co2ID = defaultCO2ID;
                }
                else
                {
                    b = Encoding.ASCII.GetBytes(s);
                    try
                    {
                        co2ID = Convert.ToInt32(s, 16);
                    }
                    catch
                    {
                        co2ID = 0;
                    }
                    s = "File CO2: " + s;
                    TextBox2Text(s + "\r\n");
                }
            }

            if (co2Calibration == 0D)
            {
                s = getFile("CO2Calibration");
                if (String.IsNullOrEmpty(s))
                {
#if false
                    string mes = "TC notfound: use default=" + defaultCO2Calibration.ToString();
                    TextBox2Text(mes + "\r\n");
#endif
                    co2Calibration = defaultCO2Calibration;
                }
                else
                {
                    b = Encoding.ASCII.GetBytes(s);
                    try
                    {
                        co2Calibration = Convert.ToDouble(s);
                    }
                    catch
                    {
                        co2Calibration = 0D;
                    }
                    s = "File Calibration: " + s;
                    TextBox2Text(s + "\r\n");
                }
            }

            if (co2Inclination == 0D)
            {
                s = getFile("CO2Inclination");
                if (String.IsNullOrEmpty(s))
                {
#if false
                    string mes = "TI notfound: use default=" + defaultCO2Inclination.ToString();
                    TextBox2Text(mes + "\r\n");
#endif
                    co2Inclination = defaultCO2Inclination;
                }
                else
                {
                    b = Encoding.ASCII.GetBytes(s);
                    try
                    {
                        co2Inclination = Convert.ToDouble(s);
                    }
                    catch
                    {
                        co2Inclination = 0D;
                    }
                    s = "File Inclination: " + s;
                    TextBox2Text(s + "\r\n");
                }
            }
        }

        private static string ProgramFilesx86()
        {
            if (8 == IntPtr.Size
                || (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432"))))
            {
                return Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            }
            return Environment.GetEnvironmentVariable("ProgramFiles");
        }

        string getFile(string fileName)
        {
            string line = null;
            string filePath;

            if (ValidateFile(new string[] {
                //AppData
                System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                     + configDir,
                //Program Files
                //System.Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                ProgramFilesx86()
                     + configDir,
                //Current Directory
                System.IO.Directory.GetCurrentDirectory()
                      + @"\"},
                fileName,
                out filePath))
            {
                Debug.WriteLine("file path <{0}>", filePath);

                try
                {
                    StreamReader reader =
                        new StreamReader(@filePath, System.Text.Encoding.Default, false);
                    line = reader.ReadLine();
                    reader.Close();
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message); //////////////////
                    return null;
                }
            }
            else
            {
                Debug.WriteLine("{0}: No file found", fileName);
            }
            return line;
        }

        private static bool ValidateFile(string[] dirArray, string name, out string file)
        {
            foreach (string dirPath in dirArray)
            {
                string filePath = dirPath + name + ".txt";
                Debug.WriteLine("filePath: <{0}>", filePath);
                if (File.Exists(filePath))
                {
                    file = filePath;
                    return true;
                }
            }
            file = null;
            return false;
        }

        string getRegistory(string rGetValueName)
        {
            string rKeyName = @"SOFTWARE\DEVDRV\CO2 Sensor";
            string s = null;

            try
            {
                RegistryKey rKey = Registry.LocalMachine.OpenSubKey(rKeyName);
                s = (string)rKey.GetValue(rGetValueName);
                rKey.Close();

                //Debug.WriteLine(s);
            }
            catch (NullReferenceException)
            {
                ; // TextBox2Text("Registory not found: " + rGetValueName + "\r\n");
            }
            return s;
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
