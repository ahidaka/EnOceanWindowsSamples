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
using System.IO.Ports;
using System.Reflection;
using System.Diagnostics;

namespace SwitchesAndSensors
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool working = false; // system status for thread control
        private bool stopped = true;  // button status
        private static Semaphore onDisplay; // Dislay control
        System.Threading.Thread timerThread;

        private System.IO.Ports.SerialPort serialPort1;
        private byte[] readBuffer;
        private byte[] writeBuffer;
        private byte[] data;
        private byte[] rpsData;
        private byte[] id;

        private LinearGradientBrush opacityMaskTranslucent;
        private LinearGradientBrush opacityMaskFullDisplay;

        static SolidColorBrush brushBlue = new SolidColorBrush(Colors.Blue);
        static SolidColorBrush brushMagenta = new SolidColorBrush(Colors.Magenta);
        static SolidColorBrush brushWhite = new SolidColorBrush(Colors.White);

        enum sw { Off, On, DontCare };
        private sw swLeft = sw.DontCare;
        private sw swRight = sw.DontCare;
        enum rctMessage
        {
            Ok = 0x58,
            Err = 0x19,
            ErrSyntaxHSeq = 0x08,
            ErrSyntaxLength = 0x09,
            ErrSyntaxOrg = 0x0B,
            ErrSyntaxChksum = 0x0A,
            ErrTxIdRange = 0x22,
            ErrIdRange = 0x22,
            Unknown = 0xFF
        }
        static readonly byte orgRPS = 0x05;
        static readonly byte org1BS = 0x06;
        static readonly byte org4BS = 0x07;

        public MainWindow()
        {
            InitializeComponent();

            readBuffer = new byte[16];
            writeBuffer = new byte[16];
            for (int i = 0; i < 12; i++)
            {
                writeBuffer[1] = 0;
            }
            writeBuffer[0] = 0xA5;
            writeBuffer[1] = 0x5A;
            writeBuffer[2] = 0x03 << 5 | 11;

            data = new byte[4];
            rpsData = new byte[2];
            id = new byte[4];
            onDisplay = new Semaphore(1, 1);

            opacityMaskTranslucent = new LinearGradientBrush();
            opacityMaskTranslucent.GradientStops.Add(new GradientStop(Color.FromArgb(64, 0, 0, 0), 0.0));
            opacityMaskFullDisplay = new LinearGradientBrush();
            opacityMaskFullDisplay.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 0.0));

            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                ListBoxItem itemNewPort = new ListBoxItem();
                itemNewPort.Content = port;
                itemNewPort.IsSelected = true;
                portSelect.Items.Add(itemNewPort);
                portSelect.ScrollIntoView(itemNewPort);
            }

            this.Closed += new EventHandler(enOceanShutdown);

            working = true;

            timerThread = new System.Threading.Thread(TimerThread);
            timerThread.Start();

            RightRectangleUpperOff();
            RightRectangleLowerOff();
            LeftRectangleUpperOff();
            LeftRectangleLowerOff();
        }

        private void TimerThread()
        {
            while (working)
            {
                while (!stopped)
                {
                    ReceiveDisplay();
                }
                System.Threading.Thread.Sleep(100); // 100msec
            }
        }

        private float EnOceanGauge(uint u)
        {
            float pressure = u / 18.9076F;
            if (pressure < 0)
            {
                pressure = 0;
            }
            return pressure;
        }

        private Nullable<Int16> WhichLevel(uint u)
        {
            Nullable<Int16> level = 8;

            if (u <= 30)
            {
                level = 1;
            }
            else if (u <= 50)
            {
                level = 2;
            }
            else if (u <= 70)
            {
                level = 3;
            }
            else if (u <= 115)
            {
                level = 4;
            }
            else if (u <= 160)
            {
                level = 5;
            }
            else if (u <= 210)
            {
                level = 6;
            }
            else
            {
                level = 7;
            }
            return level;
        }

        public void ReceiveDisplay()
        {
            uint inputValue;
            uint seq, len;
            byte org;
            uint status, checkSum, nu;
            bool gotHeader;

            //RefleshDisplay();

            if (!stopped && serialPort1.BytesToRead > 0)
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
                            serialPort1.Read(readBuffer, 0, 1);
                        }
                        catch (Exception)
                        {
                            Dispatcher.BeginInvoke(
                                new DispatcherOperationCallback(TextBox2Text),
                                    "Stopped.\n"); // read exception
                        }
                    }
                    while (readBuffer[0] != 0xA5);

                    serialPort1.Read(readBuffer, 0, 1);
                    gotHeader = readBuffer[0] == 0x5A;
                }
                while (!gotHeader);

                // OK, we got the Header
                // then read whole packet
                uint sum = 0;
                for (int i = 0; i < 11; i++)
                {
                    serialPort1.Read(readBuffer, i, 1);
                    sum += readBuffer[i];
                }
                serialPort1.Read(readBuffer, 11, 1);

                seq = (uint)readBuffer[0] >> 5;
                len = (uint)readBuffer[0] & 0x1F;
                org = readBuffer[1];
                data[3] = readBuffer[2];
                data[2] = readBuffer[3];
                data[1] = readBuffer[4];
                data[0] = readBuffer[5];
                id[0] = readBuffer[6];
                id[1] = readBuffer[7];
                id[2] = readBuffer[8];
                id[3] = readBuffer[9];

                status = (uint)readBuffer[10];
                nu = (status >> 4) & 0x01;
                checkSum = (uint)readBuffer[11];

                if (len != 11)
                {
                    // length error
                    return;
                }
                sum &= 0xFF;
                if (sum != checkSum)
                {
                    // Checksum error
                    return;
                }

                string IdText = "ID: ";
                for (int i = 0; i < 4; i++)
                {
                    IdText += id[i].ToString("X2");
                }
                IdText += "\n";

                Dispatcher.BeginInvoke(
                    new DispatcherOperationCallback(TextBox2Text),
                    IdText); // read exception

                if (seq == 4)
                {
                    // RCT received
                    Dispatcher.BeginInvoke(
                        new DispatcherOperationCallback(ResponseShow), org);
                    return;
                }
                else if (seq != 0 && seq != 1)
                {
                    // no RRT received
                    return;
                }

                if (org == 5) // RPS, locker switches
                {
                    if (nu == 0 && data[3] == 0x00)
                    {
                        Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(
                            delegate(object o)
                            {
                                RectangleSwitchEnd();
                                return null;
                            }
                            ), 5);
                        return;
                    }

                    rpsData[0] = (byte) (data[3] >> 4);
                    rpsData[1] = (byte) (data[3] & 0x0F);

                    for (int i = 0; i < 2; i++)
                    {
                        switch (rpsData[i])
                        {
                            case 0x01:
                                Dispatcher.BeginInvoke(
                                    new DispatcherOperationCallback(
                                        delegate(object o)
                                        {
                                            RightRectangleUpperOn();
                                            RightRectangleLowerOff();
                                            return null;
                                        }
                                        ), rpsData[i]);
                                swRight = sw.On;
                                break;
                            case 0x03:
                                Dispatcher.BeginInvoke(
                                    new DispatcherOperationCallback(
                                        delegate(object o)
                                        {
                                            RightRectangleUpperOff();
                                            RightRectangleLowerOn();
                                            return null;
                                        }
                                        ), rpsData[i]);
                                swRight = sw.Off;
                                break;
                            case 0x05:
                                Dispatcher.BeginInvoke(
                                    new DispatcherOperationCallback(
                                        delegate(object o)
                                        {
                                            LeftRectangleUpperOn();
                                            LeftRectangleLowerOff();
                                            return null;
                                        }
                                        ), rpsData[i]);
                                swLeft = sw.On;
                                break;
                            case 0x07:
                                Dispatcher.BeginInvoke(
                                    new DispatcherOperationCallback(
                                        delegate(object o)
                                        {
                                            LeftRectangleUpperOff();
                                            LeftRectangleLowerOn();
                                            return null;
                                        }
                                        ), rpsData[i]);
                                swLeft = sw.Off;
                                break;

                            default:
                                Debug.Print("{0} = {1}", i, rpsData[i]);
                                break;
                        }
                    }
                }

                else if (org == 7) // 4BS, sensors
                {
                    // sensor data...
                    inputValue = (uint)data[2];
                    onDisplay.WaitOne();
                    String pressureText = String.Format("{0:F2}", EnOceanGauge(inputValue)) + " g/cm2";
                    Console.WriteLine("EnOcean: " + pressureText + "\n");

                    Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(LabelEnOceanText),
                            pressureText);

                    Dispatcher.BeginInvoke(
                            new DispatcherOperationCallback(RectangleDisplay),
                            inputValue);
                    onDisplay.Release();
                }
                else
                {
                    return;
                }
            }
        }

        void LeftRectangleUpperOn()
        {
            rectangle9.Fill = brushMagenta;
            rectangle9.Stroke = brushMagenta;
            rectangle9.Visibility = System.Windows.Visibility.Visible;
            rectangle9.OpacityMask = opacityMaskFullDisplay;
        }

        void LeftRectangleUpperOff()
        {
            rectangle9.Fill = brushWhite;
            rectangle9.Stroke = brushMagenta;
            rectangle9.Visibility = System.Windows.Visibility.Visible;
            rectangle9.OpacityMask = opacityMaskFullDisplay;
        }

        void LeftRectangleLowerOn()
        {
            rectangle10.Fill = brushBlue;
            rectangle10.Stroke = brushBlue;
            rectangle10.Visibility = System.Windows.Visibility.Visible;
            rectangle10.OpacityMask = opacityMaskFullDisplay;
        }

        void LeftRectangleLowerOff()
        {
            rectangle10.Fill = brushWhite;
            rectangle10.Stroke = brushBlue;
            rectangle10.Visibility = System.Windows.Visibility.Visible;
            rectangle10.OpacityMask = opacityMaskFullDisplay;
        }

        void RightRectangleUpperOn()
        {
            rectangle11.Fill = brushMagenta;
            rectangle11.Stroke = brushMagenta;
            rectangle11.Visibility = System.Windows.Visibility.Visible;
            rectangle11.OpacityMask = opacityMaskFullDisplay;
        }

        void RightRectangleUpperOff()
        {
            rectangle11.Fill = brushWhite;
            rectangle11.Stroke = brushMagenta;
            rectangle11.Visibility = System.Windows.Visibility.Visible;
            rectangle11.OpacityMask = opacityMaskFullDisplay;
        }

        void RightRectangleLowerOn()
        {
            rectangle12.Fill = brushBlue;
            rectangle12.Stroke = brushBlue;
            rectangle12.Visibility = System.Windows.Visibility.Visible;
            rectangle12.OpacityMask = opacityMaskFullDisplay;
        }

        void RightRectangleLowerOff()
        {
            rectangle12.Fill = brushWhite;
            rectangle12.Stroke = brushBlue;
            rectangle12.Visibility = System.Windows.Visibility.Visible;
            rectangle12.OpacityMask = opacityMaskFullDisplay;
        }

        void RectangleSwitchEnd()
        {
            for (int i = 9; i <= 12; i++)
            {
                Rectangle r = GetObjectFromName(string.Format("rectangle{0}", i)) as Rectangle;
                r.OpacityMask = opacityMaskTranslucent;
            }
        }

        object LabelEnOceanText(object obj)
        {
            labelEnOcean.Content = obj as string;
            return null;
        }

        object TextBox2Text(object obj)
        {
            textBox2.Text += obj as string;
            return null;
        }

        object RectangleDisplay(object obj)
        {
            uint? inputValue = obj as uint?;
            Nullable<Int16> level = WhichLevel((uint)inputValue);

            for (int i = 1; i <= 12; i++)
            {
                Rectangle r = GetObjectFromName(string.Format("rectangle{0}", i)) as Rectangle;

                if (i <= level)
                {
                    r.Visibility = System.Windows.Visibility.Visible;
                    r.OpacityMask = opacityMaskFullDisplay;
                }
                else
                {
                    r.Visibility = System.Windows.Visibility.Hidden;
                    r.OpacityMask = opacityMaskFullDisplay;
                }
            }

            String logLine = "L " + level.ToString() + ", "
                + String.Format("{0:F2}", EnOceanGauge((uint)inputValue)) + " g/cm2\n";
            textBox2.Text += logLine;
            textBox2.ScrollToEnd();

            return null;
        }

        object RectangleDisplayWithShadow(object obj)
        {
            Nullable<Int16> level = obj as Nullable<Int16>;

            for (int i = 1; i <= 12; i++)
            {
                Rectangle r = GetObjectFromName(string.Format("rectangle{0}", i)) as Rectangle;

                if (i <= level)
                {
                    r.Visibility = System.Windows.Visibility.Visible;
                    r.OpacityMask = opacityMaskFullDisplay;
                }
                else
                {
                    r.Visibility = System.Windows.Visibility.Visible;
                    r.OpacityMask = opacityMaskTranslucent;
                }
            }

            return null;
        }

        object ResponseShow(object obj)
        {
            Nullable<byte> res = obj as Nullable<byte>;
            string s;

            switch (res)
            {
                case 0x58:
                    s = "Ok";
                    break;
                case 0x19:
                    s = "Err";
                    break;
                case 0x08:
                    s = "ErrSyntaxHSeq";
                    break;
                case 0x09:
                    s = "ErrSyntaxLength";
                    break;
                case 0x0B:
                    s = "ErrSyntaxOrg";
                    break;
                case 0x0A:
                    s = "ErrSyntaxChksum";
                    break;
                case 0x22:
                    s = "ErrTxIdRange";
                    break;
                case 0x1A:
                    s = "ErrIdRange";
                    break;
                default:
                    s = "Unknown";
                    break;
            }
            textBox1.Text = "Response: " + s;
            textBox2.Text += textBox1.Text + "\n";

            return null;
        }

        private bool serialOpen()
        {
            ListBoxItem portName = (ListBoxItem)portSelect.SelectedValue;

            serialPort1 = new SerialPort("COM11", 9600, Parity.None, 8, StopBits.One);
            serialPort1.PortName = (string)portName.Content;
            try
            {
                serialPort1.Open();
            }
            catch (Exception)
            {
                textBox1.Text = "Error: open " + serialPort1.PortName;
                return false;
            }
            textBox1.Text = "Open: " + portName.Content;
            textBox2.Text += textBox1.Text + "\n";
            return true;
        }

        private void serialClose()
        {
            serialPort1.Close();
            textBox1.Text = "Close: " + serialPort1.PortName;
            textBox2.Text += textBox1.Text + "\n";
        }

        private void serialTransmit(byte org, byte[] buffer, byte status)
        {
            textBox1.Text = "Transmit: " + serialPort1.PortName;
            textBox2.Text += textBox1.Text + "\n";

            writeBuffer[3] = org;
            for (int i = 0; i < 4; i++)
            {
                writeBuffer[i + 4] = buffer[i];
            }
            writeBuffer[12] = status;

            uint sum = 0;
            for (int i = 2; i < 13; i++)
            {
                sum += writeBuffer[i];
            }
            writeBuffer[13] = (byte)(sum & 0xFF);

            serialPort1.Write(writeBuffer, 0, 14);

            return;
        }

        private void buttonS_Click(object sender, RoutedEventArgs e)
        {
            if (stopped)
            {
                textBox2.Text = "";
                if (portSelect.SelectedIndex >= 0)
                {
                    if (!serialOpen())
                    {
                        return;
                    }
                    stopped = false;
                    buttonS.Content = "Stop";
                    labelEnOcean.Content = "";
                    RightRectangleUpperOff();
                    RightRectangleLowerOff();
                    LeftRectangleUpperOff();
                    LeftRectangleLowerOff();

                    for (int i = 1; i <= 12; i++)
                    {
                        Rectangle r = GetObjectFromName(string.Format("rectangle{0}", i)) as Rectangle;
                        r.Visibility = System.Windows.Visibility.Visible;
                        r.OpacityMask = opacityMaskTranslucent;
                    }
                    textBox1.Text = "Start: " + serialPort1.PortName;
                    swLeft = swRight = sw.DontCare;
                }
                else
                {
                    textBox1.Text = "Error! Please input COM port name.";
                }
            }
            else
            {
                stopped = true;
                serialClose();
                buttonS.Content = "Start";
                labelEnOcean.Content = "EnOcean Switches and Sensors";
                for (int i = 1; i <= 12; i++)
                {
                    Rectangle r = GetObjectFromName(string.Format("rectangle{0}", i)) as Rectangle;
                    r.Visibility = System.Windows.Visibility.Visible;
                    r.OpacityMask = opacityMaskFullDisplay;
                }
                RightRectangleUpperOff();
                RightRectangleLowerOff();
                LeftRectangleUpperOff();
                LeftRectangleLowerOff();
                swLeft = swRight = sw.DontCare;
            }
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

        private void enOceanShutdown(Object sender, EventArgs e)
        {
            working = false;
            if (!stopped)
            {
                stopped = true;
                serialPort1.Close();
            }
        }

        private void buttonL_Click(object sender, RoutedEventArgs e)
        {
            switch (swLeft)
            {
                case sw.DontCare:
                    swLeft = sw.On;
                    break;
                case sw.On:
                    swLeft = sw.Off;
                    break;
                case sw.Off:
                    swLeft = sw.DontCare;
                    break;
            }
            LeftRectangleUpperOff();
            LeftRectangleLowerOff();
            if (swLeft == sw.On)
            {
                LeftRectangleUpperOn();
            }
            else if (swLeft == sw.Off)
            {
                LeftRectangleLowerOn();
            }
        }

        private void buttonR_Click(object sender, RoutedEventArgs e)
        {
            switch (swRight)
            {
                case sw.DontCare:
                    swRight = sw.On;
                    break;
                case sw.On:
                    swRight = sw.Off;
                    break;
                case sw.Off:
                    swRight = sw.DontCare;
                    break;
            }
            RightRectangleUpperOff();
            RightRectangleLowerOff();
            if (swRight == sw.On)
            {
                RightRectangleUpperOn();
            }
            else if (swRight == sw.Off)
            {
                RightRectangleLowerOn();
            }
        }
        private void buttonT_Click(object sender, RoutedEventArgs e)
        {
            bool needClose = false;

            if (stopped)
            {
                if (!serialOpen())
                {
                    return;
                }
                needClose = true;
            }

            byte magic = 0;
            if (swLeft != sw.DontCare)
            {
                magic |= swLeft == sw.On ? (byte) 0x05 : (byte) 0x07;
            }
            if (magic != 0)
            {
                magic <<= 4;
            }
            if (swRight != sw.DontCare)
            {
                magic |= swRight == sw.On ? (byte) 0x01 : (byte) 0x03;
            }
            data[0] = (byte) magic;
            byte status = magic == 0x00 ? (byte) 0x20 : (byte) 0x30;
            serialTransmit(orgRPS, data, status);

            if (needClose)
            {
                serialClose();
            }
        }

        private void slider1_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ;
        }

        private void buttonD_Click(object sender, RoutedEventArgs e)
        {
            ;
        }
    }
}
