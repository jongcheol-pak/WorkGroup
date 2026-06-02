using System.Runtime.InteropServices;

namespace WorkGroup.App.Services;

/// <summary>
/// 알림 영역(트레이) 아이콘을 Win32 Shell_NotifyIcon으로 관리한다(plan.md T12, 의존성 없이).
/// 메시지 전용 창을 만들어 트레이 콜백을 받고, 우클릭 시 컨텍스트 메뉴(열기/종료)를 띄운다.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint WM_APP_TRAY = 0x8000 + 1; // WM_APP+1
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint NIM_ADD = 0x0;
    private const uint NIM_DELETE = 0x2;
    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const int CMD_OPEN = 1;
    private const int CMD_EXIT = 2;
    private const uint IDI_APPLICATION = 32512;

    private static readonly IntPtr HwndMessage = new(-3);

    // WndProc 델리게이트는 GC되지 않도록 필드로 보관한다.
    private readonly WndProc _wndProc;
    private IntPtr _hwnd;
    private bool _added;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public TrayIconService() => _wndProc = WindowProc;

    /// <summary>트레이 아이콘을 등록한다(UI 스레드에서 호출).</summary>
    public void Initialize()
    {
        var hInstance = GetModuleHandle(null);
        var wc = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = "WorkGroupTrayWindow"
        };
        RegisterClass(ref wc);

        _hwnd = CreateWindowEx(0, wc.lpszClassName, string.Empty, 0, 0, 0, 0, 0,
            HwndMessage, IntPtr.Zero, hInstance, IntPtr.Zero);

        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_APP_TRAY;
        data.hIcon = LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
        data.szTip = "WorkGroup";
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1
    };

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_APP_TRAY:
                var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouseMsg is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
                    OpenRequested?.Invoke();
                else if (mouseMsg == WM_RBUTTONUP)
                    ShowContextMenu();
                return IntPtr.Zero;

            case WM_COMMAND:
                var id = (int)(wParam.ToInt64() & 0xFFFF);
                if (id == CMD_OPEN) OpenRequested?.Invoke();
                else if (id == CMD_EXIT) ExitRequested?.Invoke();
                return IntPtr.Zero;

            default:
                return DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, CMD_OPEN, "열기");
        AppendMenu(menu, 0, CMD_EXIT, "종료");

        GetCursorPos(out var pt);
        // TrackPopupMenu 전에 포그라운드로 만들어야 메뉴가 정상적으로 닫힌다.
        SetForegroundWindow(_hwnd);
        TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added)
        {
            var data = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    // ----- Win32 interop -----

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
        uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
