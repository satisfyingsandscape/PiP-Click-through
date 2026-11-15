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
        StartMenuShortcut.Ensure();
        using var app = new TrayApp();
        Application.Run();
    }
}

internal static class StartMenuShortcut
{
    public static void Ensure()
    {
        try
        {
            string executable = Application.ExecutablePath;
            string programsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            string shortcutPath = Path.Combine(programsFolder, "PiP Click-through.lnk");

            Directory.CreateDirectory(programsFolder);

            // WScript.Shell is built into Windows; no installer or extra dependency is needed.
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;

            object? shell = null;
            object? shortcut = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                if (shell is null) return;

                dynamic shellObject = shell;
                shortcut = shellObject.CreateShortcut(shortcutPath);
                dynamic shortcutObject = shortcut;
                shortcutObject.TargetPath = executable;
                shortcutObject.WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory;
                shortcutObject.Description = "Make browser Picture-in-Picture windows click-through";
                shortcutObject.IconLocation = executable + ",0";
                shortcutObject.Save();
            }
            finally
            {
                if (shortcut is not null && Marshal.IsComObject(shortcut))
                    Marshal.FinalReleaseComObject(shortcut);
                if (shell is not null && Marshal.IsComObject(shell))
                    Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            // Shortcut creation is best-effort; never prevent the tray app from starting.
        }
    }
}

internal sealed class TrayApp : IDisposable
{
    private readonly Stream _iconStream;
    private readonly Icon _appIcon;
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
        var firefoxDiagnostics = new ToolStripMenuItem("Copy Firefox window diagnostics", null, (_, _) => CopyFirefoxDiagnostics());
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
        _menu.Items.Add(firefoxDiagnostics);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startup);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exit);

        _iconStream = typeof(TrayApp).Assembly.GetManifestResourceStream("PiPClickThrough.app.ico")
            ?? throw new InvalidOperationException("The application icon resource is missing.");
        _appIcon = new Icon(_iconStream);

        _tray = new NotifyIcon
        {
            Icon = _appIcon,
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
        _target = FindBrowserPipWindow();
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

    private void CopyFirefoxDiagnostics()
    {
        var lines = new List<string> { "PiP Click-through - Firefox window diagnostics", "" };
        int count = 0;

        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            string processName;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch { return true; }

            if (!processName.Equals("firefox", StringComparison.OrdinalIgnoreCase))
                return true;

            Native.GetWindowRect(hwnd, out var rect);
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            long exStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            bool topmost = (exStyle & Native.WS_EX_TOPMOST) != 0;
            IntPtr owner = Native.GetWindow(hwnd, Native.GW_OWNER);
            string className = Native.GetWindowClassName(hwnd);
            string title = Native.GetWindowTitle(hwnd);

            lines.Add("HWND: 0x" + hwnd.ToInt64().ToString("X") + " | Visible: " + Native.IsWindowVisible(hwnd));
            lines.Add("Class: " + className + " | Size: " + width + "x" + height + " | Topmost: " + topmost + " | Owner: 0x" + owner.ToInt64().ToString("X"));
            lines.Add("ExStyle: 0x" + exStyle.ToString("X") + " | Title: " + title);
            lines.Add("");
            count++;
            return true;
        }, IntPtr.Zero);

        if (count == 0)
            lines.Add("No Firefox windows were found. Make sure Firefox and its PiP window are open.");

        string report = string.Join(Environment.NewLine, lines);
        try
        {
            Clipboard.SetText(report);
            MessageBox.Show("Diagnostics copied to the clipboard (" + count + " Firefox window(s)). Paste them into the chat.",
                "Firefox diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch
        {
            MessageBox.Show(report, "Firefox diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private static IntPtr FindBrowserPipWindow()
    {
        var candidates = new List<(IntPtr Hwnd, int Area, string Title, int Priority)>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;

            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            string processName;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch { return true; }

            bool isChromeBased = processName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                                 processName.Equals("opera", StringComparison.OrdinalIgnoreCase) ||
                                 processName.Equals("opera_gx", StringComparison.OrdinalIgnoreCase);
            bool isFirefox = processName.Equals("firefox", StringComparison.OrdinalIgnoreCase);
            if (!isChromeBased && !isFirefox) return true;

            if (!Native.GetWindowRect(hwnd, out var rect)) return true;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 80 || height < 50) return true;

            if (isChromeBased)
            {
                // Chromium PiP uses a dedicated Chrome_WidgetWin_1 top-level window.
                // It is topmost, has no owner, and is much smaller than the screen.
                string className = Native.GetWindowClassName(hwnd);
                long exStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                int screenWidth = Native.GetSystemMetrics(Native.SM_CXSCREEN);
                int screenHeight = Native.GetSystemMetrics(Native.SM_CYSCREEN);

                if (!className.Equals("Chrome_WidgetWin_1", StringComparison.Ordinal) ||
                    (exStyle & Native.WS_EX_TOPMOST) == 0 ||
                    Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero ||
                    width >= screenWidth * 3 / 4 ||
                    height >= screenHeight * 3 / 4)
                    return true;
            }
            else
            {
                // Firefox's actual PiP player is MozillaDialogClass, titled
                // "Picture-in-Picture", and is marked as topmost on this system.
                // The main Firefox browser window uses MozillaWindowClass instead.
                long firefoxExStyle = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                string firefoxClass = Native.GetWindowClassName(hwnd);
                bool isTopmost = (firefoxExStyle & Native.WS_EX_TOPMOST) != 0;
                string firefoxTitle = Native.GetWindowTitle(hwnd);

                bool isFirefoxPipDialog =
                    firefoxClass.Equals("MozillaDialogClass", StringComparison.Ordinal) &&
                    firefoxTitle.Equals("Picture-in-Picture", StringComparison.OrdinalIgnoreCase) &&
                    isTopmost;

                // Keep a conservative fallback for Firefox builds that expose the
                // player with the older MozillaWindowClass window type.
                bool isLegacyFirefoxPip =
                    firefoxClass.Equals("MozillaWindowClass", StringComparison.Ordinal) &&
                    isTopmost &&
                    Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero;

                if (!isFirefoxPipDialog && !isLegacyFirefoxPip)
                    return true;

                if (width > 3000 || height > 2000) return true;
                double ratio = (double)width / height;
                if (ratio < 0.65 || ratio > 4.0) return true;
            }

            string title = Native.GetWindowTitle(hwnd);
            int priority = isFirefox
                ? (Native.GetWindowClassName(hwnd).Equals("MozillaDialogClass", StringComparison.Ordinal) ? 0 : 1)
                : 0;
            candidates.Add((hwnd, width * height, title, priority));
            return true;
        }, IntPtr.Zero);

        // Prefer the smallest matching video window to avoid selecting the browser itself.
        return candidates.OrderBy(c => c.Priority).ThenBy(c => c.Area).Select(c => c.Hwnd).FirstOrDefault();
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
        _appIcon.Dispose();
        _iconStream.Dispose();
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
    public const long WS_EX_TOPMOST = 0x00000008L;
    public const int GW_OWNER = 4;
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;
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
    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);
    public static string GetWindowClassName(IntPtr hWnd)
    {
        var className = new StringBuilder(256);
        GetClassName(hWnd, className, className.Capacity);
        return className.ToString();
    }
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
