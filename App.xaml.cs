using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace LighterSpotify
{
    public partial class App : Application
    {
        static Mutex singleInstance;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool isNew;
            singleInstance = new Mutex(true, "LighterSpotify_SingleInstance", out isNew);
            if (!isNew)
            {
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    Directory.CreateDirectory(Settings.Folder);
                    File.AppendAllText(Path.Combine(Settings.Folder, "error.log"), DateTime.Now + " " + args.Exception + Environment.NewLine);
                }
                catch { }
                args.Handled = true;
            };

            base.OnStartup(e);
            bool startHidden = Array.IndexOf(e.Args, "--minimized") >= 0;
            MainWindow window = new MainWindow(startHidden);
            if (startHidden) window.WindowState = WindowState.Minimized;
            window.Show();
        }
    }
}
