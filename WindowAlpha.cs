using System;
using System.Runtime.InteropServices;

namespace LighterSpotify
{
    // Window-wide transparency without WPF's AllowsTransparency (which forces slow software rendering)
    public static class WindowAlpha
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

        [DllImport("user32.dll")]
        static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

        const int GWL_EXSTYLE = -20;
        const long WS_EX_LAYERED = 0x80000;
        const uint LWA_ALPHA = 0x2;

        public static void Set(IntPtr hwnd, byte alpha)
        {
            long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((style & WS_EX_LAYERED) == 0) SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_LAYERED));
            SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
        }

        public static void Clear(IntPtr hwnd)
        {
            long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((style & WS_EX_LAYERED) != 0) SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style & ~WS_EX_LAYERED));
        }
    }
}
