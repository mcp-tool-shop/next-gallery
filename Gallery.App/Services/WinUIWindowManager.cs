using System.Runtime.InteropServices;
using Gallery.Domain.Routing;

namespace Gallery.App.Services;

/// <summary>
/// IWindowManager implementation for WinUI/WinAppSDK.
/// Wraps platform-specific window operations.
/// </summary>
public sealed class WinUIWindowManager : IWindowManager
{
    private readonly Window _window;
    private Action<string>? _navigateCallback;

    public WinUIWindowManager(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// Set callback for navigation requests.
    /// </summary>
    public void SetNavigateCallback(Action<string> callback)
    {
        _navigateCallback = callback;
    }

    public bool IsMinimized
    {
        get
        {
#if WINDOWS
            var hwnd = GetWindowHandle();
            if (hwnd != IntPtr.Zero)
            {
                return NativeMethods.IsIconic(hwnd);
            }
#endif
            return false;
        }
    }

    public bool IsForeground
    {
        get
        {
#if WINDOWS
            var hwnd = GetWindowHandle();
            if (hwnd != IntPtr.Zero)
            {
                var foreground = NativeMethods.GetForegroundWindow();
                return hwnd == foreground;
            }
#endif
            return true; // Assume foreground if can't detect
        }
    }

    public bool IsValid => _window != null;

    void IWindowManager.BringToFront() => TryBringToFront();
    void IWindowManager.RestoreFromMinimized() => TryRestoreFromMinimized();
    void IWindowManager.FlashTaskbar() => TryFlashTaskbar();

    public bool TryBringToFront()
    {
#if WINDOWS
        try
        {
            var hwnd = GetWindowHandle();
            if (hwnd == IntPtr.Zero) return false;

            var result = NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.BringWindowToTop(hwnd);

            // SetForegroundWindow can fail due to Windows focus restrictions
            // (e.g., app not in foreground, focus lock timeout not expired)
            // Flash taskbar as fallback when focus fails
            if (!result)
            {
                TryFlashTaskbar();
            }
            return result;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    public bool TryRestoreFromMinimized()
    {
#if WINDOWS
        try
        {
            var hwnd = GetWindowHandle();
            if (hwnd == IntPtr.Zero) return false;

            var showResult = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
            var focusResult = NativeMethods.SetForegroundWindow(hwnd);

            // Flash as fallback if focus fails
            if (!focusResult)
            {
                TryFlashTaskbar();
            }
            return showResult;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    public bool TryFlashTaskbar()
    {
#if WINDOWS
        try
        {
            var hwnd = GetWindowHandle();
            if (hwnd == IntPtr.Zero) return false;

            var flashInfo = new NativeMethods.FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.FLASHWINFO>(),
                hwnd = hwnd,
                dwFlags = NativeMethods.FLASHW_ALL | NativeMethods.FLASHW_TIMERNOFG,
                uCount = 3,
                dwTimeout = 0
            };
            return NativeMethods.FlashWindowEx(ref flashInfo);
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }

    public void NavigateTo(string view)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _navigateCallback?.Invoke(view);
        });
    }

#if WINDOWS
    private IntPtr GetWindowHandle()
    {
        try
        {
            var nativeWindow = _window.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (nativeWindow == null) return IntPtr.Zero;

            return WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
#endif

#if WINDOWS
    private static class NativeMethods
    {
        public const int SW_RESTORE = 9;
        public const uint FLASHW_ALL = 3;
        public const uint FLASHW_TIMERNOFG = 12;

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [StructLayout(LayoutKind.Sequential)]
        public struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }
    }
#endif
}
