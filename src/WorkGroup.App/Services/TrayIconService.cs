using System.Runtime.InteropServices;

namespace WorkGroup.App.Services;

/// <summary>
/// 알림 영역(트레이) 아이콘을 Win32 Shell_NotifyIcon으로 관리한다(의존성 없이).
/// 숨김 top-level 창으로 트레이 콜백과 작업 표시줄 재생성 신호(TaskbarCreated)를 받고,
/// 우클릭 시 컨텍스트 메뉴(열기/종료)를 띄운다.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint WM_APP_TRAY = 0x8000 + 1; // WM_APP+1
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_LBUTTONUP = 0x0202;
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
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;
    private const uint WS_EX_TOOLWINDOW = 0x80; // top-level 트레이 창을 작업 표시줄/Alt+Tab에서 숨긴다.

    // WndProc 델리게이트는 GC되지 않도록 필드로 보관한다.
    private readonly WndProc _wndProc;
    private IntPtr _hwnd;
    // 파일에서 로드한 트레이 아이콘 핸들(소유 — Dispose에서 DestroyIcon). 폴백(시스템 아이콘)이면 0으로 둔다.
    private IntPtr _ownedIcon;
    // 트레이에 표시 중인 아이콘 핸들(owned 또는 시스템 공유). 재등록 시 재로드 없이 재사용한다.
    private IntPtr _hIcon;
    // 작업 표시줄 재생성 시 셸이 보내는 "TaskbarCreated" broadcast 메시지 ID(RegisterWindowMessage로 확보).
    private uint _taskbarCreatedMsg;
    private bool _added;
    private bool _disposed;

    public event Action? OpenRequested;
    public event Action? ExitRequested;
    /// <summary>트레이 아이콘 좌클릭(단일) 시 발생. 폴더 목록 팝업을 띄우는 데 쓴다.</summary>
    public event Action? LeftClickRequested;

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

        // 부모 NULL의 top-level 창으로 만든다. 메시지 전용 창(HWND_MESSAGE)은 broadcast 메시지를
        // 받지 못해, 작업 표시줄(explorer) 재시작 시 셸이 보내는 "TaskbarCreated" 신호를 놓친다(MSDN Window Features).
        // WS_EX_TOOLWINDOW + 비표시(WS_VISIBLE 미부여) + 0 크기로 화면/작업 표시줄/Alt+Tab에 노출하지 않는다.
        _hwnd = CreateWindowEx(WS_EX_TOOLWINDOW, wc.lpszClassName, string.Empty, 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);

        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        _hIcon = LoadTrayIcon();
        AddIcon();
    }

    /// <summary>트레이 아이콘을 등록한다. 최초 등록과 작업 표시줄 재생성 후 재등록에서 공통 사용한다.</summary>
    private void AddIcon()
    {
        // 재등록 경로: explorer 재시작 후엔 이전 등록이 이미 사라져 NIM_DELETE가 실패(무해)하지만,
        // 드물게 이전 등록이 남아 있는 경우의 NIM_ADD 중복(ERROR_ALREADY_EXISTS)을 예방한다.
        if (_added)
        {
            var stale = CreateData();
            Shell_NotifyIcon(NIM_DELETE, ref stale);
            _added = false;
        }

        var data = CreateData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_APP_TRAY;
        data.hIcon = _hIcon;
        data.szTip = LocalizationService.Current?.Get("App_DisplayName") ?? "WorkGroup";
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    /// <summary>앱 아이콘(Assets\AppIcon.ico)을 작은 아이콘 크기로 로드한다. 실패 시 시스템 기본 아이콘으로 폴백.</summary>
    private IntPtr LoadTrayIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(path))
            {
                var hIcon = LoadImage(IntPtr.Zero, path, IMAGE_ICON,
                    GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE);
                if (hIcon != IntPtr.Zero)
                {
                    _ownedIcon = hIcon; // 소유 핸들 — Dispose에서 해제
                    return hIcon;
                }
            }
        }
        catch (Exception)
        {
            // 경로/로드 실패는 폴백으로 처리(트레이는 계속 표시).
        }
        return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION); // 시스템 공유 아이콘(해제 불필요)
    }

    private NOTIFYICONDATA CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1
    };

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // 작업 표시줄(explorer) 재시작 시 셸이 보내는 broadcast. 트레이가 새로 생성됐으므로 아이콘을 다시 등록한다.
        if (!_disposed && msg == _taskbarCreatedMsg && _taskbarCreatedMsg != 0)
        {
            AddIcon();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case WM_APP_TRAY:
                var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
                // 좌클릭(단일)은 폴더 목록 팝업을 띄운다. 메인 창은 우클릭 메뉴 "열기"로만 연다.
                if (mouseMsg == WM_LBUTTONUP)
                    LeftClickRequested?.Invoke();
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
        var loc = LocalizationService.Current;
        AppendMenu(menu, 0, CMD_OPEN, loc?.Get("Common_Open") ?? "Open");
        AppendMenu(menu, 0, CMD_EXIT, loc?.Get("Tray_Exit") ?? "Exit");

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
        if (_ownedIcon != IntPtr.Zero)
        {
            DestroyIcon(_ownedIcon);
            _ownedIcon = IntPtr.Zero;
        }
        _hIcon = IntPtr.Zero; // owned면 위에서 해제됨, 공유 아이콘이면 해제 불필요 — 참조만 정리.
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

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
