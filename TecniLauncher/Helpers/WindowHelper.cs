using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TecniLauncher.Helpers
{
    public static class WindowHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Rect { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public Win32Rect rcMonitor;
            public Win32Rect rcWork;
            public uint dwFlags;
        }
        public static System.Windows.Rect ObtenerAreaTrabajo(Window ventana)
        {
            IntPtr hwnd = new WindowInteropHelper(ventana).Handle;
            IntPtr monitor = MonitorFromWindow(hwnd, 2);

            var info = new MonitorInfo { cbSize = Marshal.SizeOf(typeof(MonitorInfo)) };
            GetMonitorInfo(monitor, ref info);

            return new System.Windows.Rect(
                info.rcWork.left,
                info.rcWork.top,
                info.rcWork.right - info.rcWork.left,
                info.rcWork.bottom - info.rcWork.top);
        }

    }
}