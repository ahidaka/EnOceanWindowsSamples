using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace EnOceanSample
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        private static System.Threading.Mutex mutex;

        private void ApplicationStartup(object sender, StartupEventArgs e)
        {
            mutex = new System.Threading.Mutex(false, "MultiDisplay-WPF-Application");
            if (!mutex.WaitOne(0, false))
            {
                MessageBox.Show("Already started");
                mutex.Close();
                mutex = null;
                this.Shutdown();
            }
        }

        private void ApplicationExit(object sender, ExitEventArgs e)
        {
            if (mutex != null)
            {
                mutex.ReleaseMutex();
                mutex.Close();
            }
        }
    }
}
