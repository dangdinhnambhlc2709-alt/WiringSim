using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using WiringSim.UI.Services;

namespace WiringSim.UI.Helpers;

/// <summary>
/// Trợ giúp đồng bộ Win32 cho các cửa sổ Custom WindowChrome (MainWindow &amp; PlcLadderWindow):
/// - Xử lý WM_GETMINMAXINFO theo vùng làm việc rcWork của đúng màn hình chứa cửa sổ (hỗ trợ đa màn hình &amp; mọi DPI).
/// - Đồng bộ DWM Dark/Light Mode và bo góc Windows 11 mượt mà khi chuyển giữa Normal / Maximized / Minimized.
/// </summary>
public static class WindowChromeHelper
{
    private const int WM_GETMINMAXINFO = 0x0024;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public WinPoint PtReserved;
        public WinPoint PtMaxSize;
        public WinPoint PtMaxPosition;
        public WinPoint PtMinTrackSize;
        public WinPoint PtMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int CbSize;
        public WinRect RcMonitor;
        public WinRect RcWork;
        public int DwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    public static void Attach(
        Window window,
        Border outerBorder,
        Border titleBarBorder,
        Border? statusBarBorder,
        Button maximizeRestoreButton)
    {
        window.SourceInitialized += (_, _) =>
        {
            ThemeManager.ApplyWindowDwmDarkMode(window, ThemeManager.IsEffectiveDark);
            ApplyCornerAndBorderState(window, outerBorder, titleBarBorder, statusBarBorder, maximizeRestoreButton);

            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                HwndSource.FromHwnd(hwnd)?.AddHook(WindowProc);
            }
        };

        window.StateChanged += (_, _) =>
        {
            ApplyCornerAndBorderState(window, outerBorder, titleBarBorder, statusBarBorder, maximizeRestoreButton);
        };
    }

    public static void ApplyCornerAndBorderState(
        Window window,
        Border outerBorder,
        Border titleBarBorder,
        Border? statusBarBorder,
        Button maximizeRestoreButton)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            // Giữ nguyên khung viền khi thu nhỏ xuống Taskbar để khôi phục mượt không nháy viền
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            ThemeManager.ApplyWindowCornerPreference(window, ThemeManager.DWMWCP_DONOTROUND);
            outerBorder.Margin = new Thickness(0);
            outerBorder.BorderThickness = new Thickness(0);
            outerBorder.CornerRadius = new CornerRadius(0);
            titleBarBorder.CornerRadius = new CornerRadius(0);
            if (statusBarBorder is not null) statusBarBorder.CornerRadius = new CornerRadius(0);
            maximizeRestoreButton.Content = "❐";
        }
        else
        {
            ThemeManager.ApplyWindowCornerPreference(window, ThemeManager.DWMWCP_ROUND);
            outerBorder.Margin = new Thickness(0);
            outerBorder.BorderThickness = new Thickness(1);
            outerBorder.CornerRadius = new CornerRadius(8);
            titleBarBorder.CornerRadius = new CornerRadius(7, 7, 0, 0);
            if (statusBarBorder is not null) statusBarBorder.CornerRadius = new CornerRadius(0, 0, 7, 7);
            maximizeRestoreButton.Content = "☐";
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MonitorInfo { CbSize = Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                    WinRect work = info.RcWork;
                    WinRect mon = info.RcMonitor;

                    mmi.PtMaxPosition.X = Math.Abs(work.Left - mon.Left);
                    mmi.PtMaxPosition.Y = Math.Abs(work.Top - mon.Top);
                    mmi.PtMaxSize.X = Math.Abs(work.Right - work.Left);
                    mmi.PtMaxSize.Y = Math.Abs(work.Bottom - work.Top);

                    Marshal.StructureToPtr(mmi, lParam, true);
                    handled = true;
                }
            }
        }
        return IntPtr.Zero;
    }
}
