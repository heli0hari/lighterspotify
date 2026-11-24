using System;
using System.Diagnostics;
using System.IO;

namespace LighterSpotify
{
    public class LibrespotHost
    {
        Process process;
        bool waitingForLogin;
        StreamWriter log;
        object logLock = new object();

        // Raised (on a background thread) when librespot prints an error
        public event Action<string> ErrorLogged;

        public static string LogFile
        {
            get { return Path.Combine(Settings.Folder, "librespot.log"); }
        }

        static string CacheFolder
        {
            get { return Path.Combine(Settings.Folder, "librespot-cache"); }
        }

        static string CredentialsFile
        {
            get { return Path.Combine(CacheFolder, "credentials.json"); }
        }

        public bool IsRunning
        {
            get { return process != null && !process.HasExited; }
        }

        // Returns an error message, or null if it started fine
        public string Start(Settings settings)
        {
            if (IsRunning) return null;

            string exe = FindExe(settings.LibrespotPath);
            if (exe == null) return "librespot.exe not found. Set its path in settings.";

            Directory.CreateDirectory(CacheFolder);
            bool firstLogin = !File.Exists(CredentialsFile);

            string args = "--name \"" + settings.DeviceName + "\""
                + " --device-type computer"
                + " --bitrate " + settings.Bitrate
                + " --cache \"" + CacheFolder + "\""
                + " --disable-audio-cache"
                + " --initial-volume 50";

            ProcessStartInfo info = new ProcessStartInfo(exe, args);
            if (firstLogin)
            {
                // First run: show the console so you can finish librespot's own Spotify login
                info.Arguments += " --enable-oauth";
                info.UseShellExecute = true;
            }
            else
            {
                // Hidden: capture its output into librespot.log so problems can be diagnosed
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
            }

            try
            {
                process = Process.Start(info);
            }
            catch (Exception ex)
            {
                return "Couldn't start librespot: " + ex.Message;
            }

            if (!firstLogin)
            {
                OpenLog();
                process.OutputDataReceived += OnOutput;
                process.ErrorDataReceived += OnOutput;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            waitingForLogin = firstLogin;
            return null;
        }

        // After the one-time login, restart librespot hidden
        public void RestartIfLoginFinished(Settings settings)
        {
            if (!waitingForLogin || !File.Exists(CredentialsFile)) return;
            waitingForLogin = false;
            Stop();
            Start(settings);
        }

        public void Stop()
        {
            try
            {
                if (IsRunning) process.Kill(true);
            }
            catch { }
            process = null;
            CloseLog();
        }

        void OpenLog()
        {
            lock (logLock)
            {
                try
                {
                    CloseLog();
                    log = new StreamWriter(LogFile, false);
                    log.AutoFlush = true;
                }
                catch
                {
                    log = null;
                }
            }
        }

        void CloseLog()
        {
            lock (logLock)
            {
                if (log != null) log.Dispose();
                log = null;
            }
        }

        void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            lock (logLock)
            {
                if (log != null) log.WriteLine(e.Data);
            }
            if (e.Data.Contains(" ERROR ") && ErrorLogged != null) ErrorLogged(e.Data);
        }

        static string FindExe(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;

            string local = Path.Combine(AppContext.BaseDirectory, "librespot.exe");
            if (File.Exists(local)) return local;

            string cargo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "librespot.exe");
            if (File.Exists(cargo)) return cargo;

            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathVar.Split(';'))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), "librespot.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }
    }
}
