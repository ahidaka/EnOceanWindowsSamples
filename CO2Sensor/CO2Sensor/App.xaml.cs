using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

namespace CO2Sensor
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        private static System.Threading.Mutex mutex = null;
        private MainWindow window;

#if false
        private void ApplicationStartup(object sender, StartupEventArgs e)
        {
            mutex = new System.Threading.Mutex(false, "CO2-WPF-Application");
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
#endif

        /// <summary>
        /// アプリケーションが開始される時のイベント。
        /// </summary>
        /// <param name="e">イベント データ。</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            mutex = new System.Threading.Mutex(false, "CO2-WPF-Application");
            if (!App.mutex.WaitOne(0, false))
            {
                MessageBox.Show("CO2 Sensor: Already started");
                mutex.Close();
                mutex = null;
                this.Shutdown();
                return;
            }
            window = new MainWindow();
            window.Show();
        }

        /// <summary>
        /// アプリケーションが終了する時のイベント。
        /// </summary>
        /// <param name="e">イベント データ。</param>
        protected override void OnExit(ExitEventArgs e)
        {
            if (mutex != null)
            {
                window.working = false;
                window.stopped = true;

                mutex.ReleaseMutex();
                mutex.Close();
                mutex = null;
            }
        }
    }
}
