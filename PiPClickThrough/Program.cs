using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PiPClickThrough;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var app = new TrayApp();
        Application.Run();
    }
}

internal sealed class TrayApp : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _toggle;
    private readonly ToolStripMenuItem _startup;
    private readonly HotkeyWindow _hotkey;
    private readonly System.Windows.Forms.Timer _timer;
    private IntPtr _target;
    private long _originalExStyle;
    private bool _hasOriginalStyle;
    private bool _clickThrough;
    private bool _disposed;

    private const int HotkeyId = 0x5049;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValue = "PiPClickThrough";

    public TrayApp()
    {
        _status = new ToolStripMenuItem("Status: waiting for a PiP window") { Enabled = false };
        _toggle = new ToolStripMenuItem("Enable click-through (Ctrl + Alt + P)", null, (_, _) => Toggle());
        var detect = new ToolStripMenuItem("Find PiP window", null, (_, _) => DetectAndReport());
        var restore = new ToolStripMenuItem("Restore mouse input", null, (_, _) => DisableClickThrough());
        _startup = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleStartup())
        {
            Checked = IsStartupEnabled(),
            CheckOnClick = false
        };
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => Exit());

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_status);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_toggle);
        _menu.Items.Add(restore);
        _menu.Items.Add(detect);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startup);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exit);

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PiP Click-through",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Toggle();

        _hotkey = new HotkeyWindow(HotkeyId, Toggle);
        if (!Native.RegisterHotKey(_hotkey.Handle, HotkeyId,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.P))
        {
            _tray.ShowBalloonTip(5000, "PiP Click-through",
                "Could not register Ctrl + Alt + P. The shortcut may already be in use.",
                ToolTipIcon.Warning);
        }

        _timer = new System.Windows.Forms.Timer { Interval = 1200 };
        _timer.Tick += (_, _) => CheckTarget();
        _timer.Start();
        UpdateMenu();
    }

    private void Toggle()
    {
        if (_clickThrough)
        {
            DisableClickThrough();
            return;
        }

        if (!Native.IsWindow(_target))
            _target = FindBrowserPipWindow();

        if (_target == IntPtr.Zero)
        {
            _tray.ShowBalloonTip(3500, "PiP window not found",
                "Open a video in Picture-in-Picture mode in Opera, Chrome, or Firefox, then try again.",
                ToolTipIcon.Info);
            UpdateMenu();
            return;
        }

        try
        {
            _originalExStyle = Native.GetWindowLongPtr(_target, Native.GWL_EXSTYLE).ToInt64();
            _hasOriginalStyle = true;

            long newStyle = _originalExStyle | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
            Native.SetWindowLongPtr(_target, Native.GWL_EXSTYLE, new IntPtr(newStyle));
            // Alpha 255 = opaque. Layered + transparent makes the window pass mouse input through.
            if (!Native.SetLayeredWindowAttributes(_target, 0, 255, Native.LWA_ALPHA))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            Native.SetWindowPos(_target, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
            _clickThrough = true;
            _tray.ShowBalloonTip(1800, "PiP Click-through enabled",
                "Mouse clicks now pass through the video. Press Ctrl + Alt + P to restore them.",
                ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            RestoreOriginalStyle();
            _clickThrough = false;
            _tray.ShowBalloonTip(5000, "Could not enable click-through",
                ex.Message, ToolTipIcon.Error);
        }
        UpdateMenu();
    }

    private void DisableClickThrough()
    {
        RestoreOriginalStyle();
        _clickThrough = false;
        UpdateMenu();
    }

    private void RestoreOriginalStyle()
    {
        if (_hasOriginalStyle && Native.IsWindow(_target))
        {
            Native.SetWindowLongPtr(_target, Native.GWL_EXSTYLE, new IntPtr(_originalExStyle));
            Native.SetWindowPos(_target, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }
        _hasOriginalStyle = false;
    }

    private void CheckTarget()
    {
        if (_clickThrough && !Native.IsWindow(_target))
        {
            _target = IntPtr.Zero;
            _hasOriginalStyle = false;
            _clickThrough = false;
            UpdateMenu();
            _tray.ShowBalloonTip(2500, "PiP window closed",
                "Click-through has been disabled.", ToolTipIcon.Info);
        }
    }

    private void DetectAndReport()
    {
        _target = FindOperaPipWindow();
        if (_target == IntPtr.Zero)
        {
            _tray.ShowBalloonTip(3000, "No PiP window detected",
                "Make sure a video is open in Picture-in-Picture mode in Opera, Chrome, or Firefox.", ToolTipIcon.Info);
        }
        else
        {
            string title = Native.GetWindowTitle(_target);
            _tray.ShowBalloonTip(3000, "PiP window detected",
                string.IsNullOrWhiteSpace(title) ? "The video window was found." : title,
                ToolTipIcon.Info);
        }
        UpdateMenu();
    }

    private static IntPtr FindOperaPipWindow()
    {
        var candidates = new List<(IntPtr Hwnd, int Area, string Title)>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                using var process = Process.GetProcessById((int)pid);
                string name = process.ProcessName;
                // The top-level PiP window belongs to the browser process in these browsers.
                // Keep this list explicit so unrelated applications are never targeted.
                if (!name.Equals("opera", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("opera_gx", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("chrome", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("firefox", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { return true; }

            Native.GetWindowRect(hwnd, out var rect);
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 220 || height < 120 || width > 1800 || height > 1100) return true;

            long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
            // Browser PiP windows are popup windows; the main browser window is normally overlapped.
            if ((style & Native.WS_POPUP) == 0 || (style & Native.WS_CHILD) != 0) return true;

            string title = Native.GetWindowTitle(hwnd);
            // Prefer a video-like landscape popup belonging to one of the supported browsers.
            double ratio = (double)width / height;
            if (ratio < 1.15 || ratio > 2.6) return true;

            candidates.Add((hwnd, width * height, title));
            return true;
        }, IntPtr.Zero);

        return candidates.OrderBy(c => c.Area).Select(c => c.Hwnd).FirstOrDefault();
    }

    private void ToggleStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (IsStartupEnabled())
                key.DeleteValue(AppValue, false);
            else
                key.SetValue(AppValue, $"\"{Application.ExecutablePath}\"");
            _startup.Checked = IsStartupEnabled();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "PiP Click-through", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppValue) is string;
    }

    private void UpdateMenu()
    {
        _status.Text = _clickThrough ? "Status: click-through enabled"
            : Native.IsWindow(_target) ? "Status: PiP window detected"
            : "Status: waiting for a PiP window";
        _toggle.Text = _clickThrough ? "Disable click-through (Ctrl + Alt + P)"
            : "Enable click-through (Ctrl + Alt + P)";
        _startup.Checked = IsStartupEnabled();
        _tray.Text = _clickThrough ? "PiP Click-through — enabled" : "PiP Click-through";
    }

    private void Exit()
    {
        DisableClickThrough();
        Application.Exit();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        DisableClickThrough();
        Native.UnregisterHotKey(_hotkey.Handle, HotkeyId);
        _hotkey.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Action _callback;
    public HotkeyWindow(int id, Action callback)
    {
        _callback = callback;
        CreateHandle(new CreateParams { Caption = "PiPClickThroughHotkeyWindow" });
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            _callback();
            return;
        }
        base.WndProc(ref m);
    }

    public new void Dispose() => DestroyHandle();
}

internal static class Native
{
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_POPUP = 0x80000000L;
    public const long WS_CHILD = 0x40000000L;
    public const long WS_EX_TRANSPARENT = 0x00000020L;
    public const long WS_EX_LAYERED = 0x00080000L;
    public const uint LWA_ALPHA = 0x2;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : new IntPtr(SetWindowLong32(hWnd, nIndex, value));
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static string GetWindowTitle(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        var sb = new StringBuilder(Math.Max(length + 1, 256));
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
