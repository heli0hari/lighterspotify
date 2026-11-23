using System;
using System.Runtime.InteropServices;

namespace LighterSpotify
{
    // Blurs whatever is behind the window (Windows 10/11). The app draws its own dark tint on top.
    // Unlike Windows 11's Mica/Acrylic backdrops, this blur stays on when the window isn't focused.
    public static class Glass
    {
        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        const int WCA_ACCENT_POLICY = 19;
        const int ACCENT_DISABLED = 0;
        const int ACCENT_ENABLE_BLURBEHIND = 3;

        public static void Set(IntPtr hwnd, bool enabled)
        {
            if (hwnd == IntPtr.Zero) return;

            AccentPolicy accent = new AccentPolicy();
            accent.AccentState = enabled ? ACCENT_ENABLE_BLURBEHIND : ACCENT_DISABLED;

            int size = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                WindowCompositionAttributeData data = new WindowCompositionAttributeData();
                data.Attribute = WCA_ACCENT_POLICY;
                data.Data = accentPtr;
                data.SizeOfData = size;
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
    }
}
