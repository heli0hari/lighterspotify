using System;
using System.IO;

namespace LighterSpotify
{
    // Creates/refreshes a Start menu shortcut on every launch, so the app is always searchable in Start
    public static class StartMenuShortcut
    {
        public static void Ensure()
        {
            try
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string path = Path.Combine(folder, "LighterSpotify.lnk");
                string exe = Environment.ProcessPath;

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic link = shell.CreateShortcut(path);
                link.TargetPath = exe;
                link.WorkingDirectory = Path.GetDirectoryName(exe);
                link.IconLocation = exe + ",0";
                link.Description = "LighterSpotify mini-player";
                link.Save();
            }
            catch { }
        }
    }
}
