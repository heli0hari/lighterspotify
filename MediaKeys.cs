using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LighterSpotify
{
    public class MediaKeys
    {
        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        const int WM_HOTKEY = 0x0312;
        const int PlayPauseId = 9001;
        const int NextId = 9002;
        const int PreviousId = 9003;

        public event Action PlayPausePressed;
        public event Action NextPressed;
        public event Action PreviousPressed;

        IntPtr handle;
        HwndSource source;
        bool registered;

        public void Register(Window window)
        {
            if (registered) return;

            handle = new WindowInteropHelper(window).Handle;
            if (source == null)
            {
                source = HwndSource.FromHwnd(handle);
                source.AddHook(WndProc);
            }

            RegisterHotKey(handle, PlayPauseId, 0, 0xB3);
            RegisterHotKey(handle, NextId, 0, 0xB0);
            RegisterHotKey(handle, PreviousId, 0, 0xB1);
            registered = true;
        }

        public void Unregister()
        {
            if (!registered) return;
            UnregisterHotKey(handle, PlayPauseId);
            UnregisterHotKey(handle, NextId);
            UnregisterHotKey(handle, PreviousId);
            registered = false;
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == PlayPauseId && PlayPausePressed != null) PlayPausePressed();
                if (id == NextId && NextPressed != null) NextPressed();
                if (id == PreviousId && PreviousPressed != null) PreviousPressed();
                handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
