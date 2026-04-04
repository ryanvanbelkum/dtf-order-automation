using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace DtfOrderAutomation.Services;

/// <summary>
/// Native Win32 system-tray icon. Runs its own STA message loop on a background thread
/// so it works in unpackaged WinUI 3 apps without any third-party dependencies.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    // ── Win32 constants ────────────────────────────────────────────────────
    private const int WM_APP         = 0x8000;
    private const int WM_TRAYICON   = WM_APP + 1;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP  = 0x0205;
    private const int NIM_ADD       = 0;
    private const int NIM_DELETE    = 2;
    private const int NIF_MESSAGE   = 0x01;
    private const int NIF_ICON      = 0x02;
    private const int NIF_TIP       = 0x04;
    private const int MF_STRING     = 0x0000;
    private const int MF_SEPARATOR  = 0x0800;
    private const int MF_GRAYED     = 0x0001;
    private const int TPM_RIGHTBUTTON = 0x0002;
    private const int TPM_RETURNCMD   = 0x0100;
    private const int IDM_OPEN      = 1001;
    private const int IDM_RUNNOW    = 1002;
    private const int IDM_QUIT      = 1003;

    // ── Win32 structs ──────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int    cbSize;
        public IntPtr hWnd;
        public int    uID;
        public int    uFlags;
        public int    uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int    dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int    uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int    dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int    cbSize, style;
        public IntPtr lpfnWndProc;
        public int    cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public int    message;
        public IntPtr wParam, lParam;
        public int    time;
        public POINT  pt;
    }

    // ── P/Invoke ───────────────────────────────────────────────────────────
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA pnid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, int uFlags, int x, int y,
        int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int  GetMessage(out MSG lpMsg, IntPtr hWnd, int min, int max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpmsg);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_QUIT = 0x0012;

    // ── Fields ─────────────────────────────────────────────────────────────
    private readonly DispatcherQueue   _ui;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private WndProcDelegate            _wndProc = null!; // kept alive to prevent GC
    private IntPtr                     _msgWnd;
    private IntPtr                     _hIcon;
    private bool                       _disposed;

    public event Action? OpenRequested;
    public event Action? RunNowRequested;
    public event Action? QuitRequested;

    public TrayIconService(DispatcherQueue uiDispatcherQueue)
    {
        _ui = uiDispatcherQueue;

        var t = new Thread(RunMessageLoop) { IsBackground = true, Name = "TrayIconThread" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    // ── Message loop (runs on dedicated STA thread) ────────────────────────
    private void RunMessageLoop()
    {
        _hIcon   = BuildIcon();
        _wndProc = WndProc;

        var wc = new WNDCLASSEX
        {
            cbSize        = Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc   = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = "DtfTrayMsgWnd_" + Environment.ProcessId,
        };
        RegisterClassEx(ref wc);

        // HWND_MESSAGE (-3) = message-only window, never visible
        _msgWnd = CreateWindowEx(0, wc.lpszClassName, "", 0, 0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        var nid = BuildNid();
        Shell_NotifyIcon(NIM_ADD, ref nid);

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var evt = (int)(lParam.ToInt64() & 0xFFFF);
            if (evt == WM_LBUTTONDBLCLK)
                _ui.TryEnqueue(() => OpenRequested?.Invoke());
            else if (evt == WM_RBUTTONUP)
                ShowContextMenu(hWnd);
        }
        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu(IntPtr hWnd)
    {
        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING,    IDM_OPEN,   "Open");
        AppendMenu(hMenu, MF_STRING,    IDM_RUNNOW, "Run Now");
        AppendMenu(hMenu, MF_SEPARATOR, 0,           "");
        AppendMenu(hMenu, MF_STRING,    IDM_QUIT,   "Quit");

        GetCursorPos(out var pt);
        SetForegroundWindow(hWnd);
        var cmd = TrackPopupMenu(hMenu, TPM_RIGHTBUTTON | TPM_RETURNCMD,
                                 pt.X, pt.Y, 0, hWnd, IntPtr.Zero);
        DestroyMenu(hMenu);

        switch (cmd)
        {
            case IDM_OPEN:   _ui.TryEnqueue(() => OpenRequested?.Invoke());   break;
            case IDM_RUNNOW: _ui.TryEnqueue(() => RunNowRequested?.Invoke()); break;
            case IDM_QUIT:   _ui.TryEnqueue(() => QuitRequested?.Invoke());   break;
        }
    }

    // ── Icon builder ───────────────────────────────────────────────────────
    private static IntPtr BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g   = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        g.FillEllipse(new SolidBrush(Color.FromArgb(10, 132, 255)), 1, 1, 30, 30);
        g.FillPolygon(Brushes.White, new PointF[] { new(11, 8), new(11, 24), new(25, 16) });
        return bmp.GetHicon();
    }

    private NOTIFYICONDATA BuildNid() => new()
    {
        cbSize           = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd             = _msgWnd,
        uID              = 1,
        uFlags           = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_TRAYICON,
        hIcon            = _hIcon,
        szTip            = "DTF Order Automation",
    };

    // ── Cleanup ────────────────────────────────────────────────────────────
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_msgWnd != IntPtr.Zero)
        {
            var nid = BuildNid();
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            PostMessage(_msgWnd, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
