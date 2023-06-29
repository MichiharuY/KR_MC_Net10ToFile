using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace _01_NET10収録_NET10toFILE_
{
    /// <summary>
    /// App.xaml の相互作用ロジック
    /// </summary>
    public partial class App : Application
    {
        // 二重起動防止処理用
        private Mutex mutex = new Mutex(false, "01_NET10収録(NET10toFILE)");

        /// <summary>
        /// System.Windows.Application.Startup イベント を発生させます。
        /// </summary>
        /// <param name="e">イベントデータ を格納している StartupEventArgs</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            if (mutex.WaitOne(0, false) == false)
            {
                // すでに起動しているので終了する
                mutex.Close();
                mutex = null;
                this.Shutdown();
                base.OnStartup(e);
            }
            else
            {
                base.OnStartup(e);
                this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
        }

        /// <summary>
        /// System.Windows.Application.Exit イベント を発生させます。
        /// </summary>
        /// <param name="e">イベントデータ を格納している ExitEventArgs</param>
        protected override void OnExit(ExitEventArgs e)
        {
            if (mutex != null)
            {
                mutex.ReleaseMutex();
                mutex.Close();
                base.OnExit(e);
            }
            else
            {
                base.OnExit(e);
            }
        }
    }
}
