using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace WorkGroup.App.Interop;

/// <summary>
/// 셸이 제공하는 IDataObject(Shell IDList 포함)로 Win32 OLE 드래그를 시작한다.
/// WinUI의 DataPackage 드래그는 CF_HDROP만 실어 작업 표시줄 핀이 받지 못하므로,
/// 탐색기와 동일한 셸 데이터로 드래그해 핀을 가능하게 한다(plan.md T2 C4 / D1).
/// </summary>
public static class ShellFileDragSource
{
    private const int DROPEFFECT_COPY = 1;
    private const int DROPEFFECT_LINK = 4;

    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
    private static readonly Guid IID_IDataObject = new("0000010e-0000-0000-C000-000000000046");
    private static readonly Guid BHID_DataObject = new("B8C0BD9F-ED24-455c-83E6-D5390C4FE8C4");

    /// <summary>지정 파일(.lnk)에 대해 셸 드래그를 시작한다. 사용자가 작업 표시줄에 드롭하면 핀된다.</summary>
    public static void BeginDrag(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("드래그할 파일이 없습니다.", filePath);

        // WinUI UI 스레드에서 OLE 드래그를 쓰려면 OLE 초기화가 필요(S_FALSE=이미 초기화도 정상).
        OleInitialize(IntPtr.Zero);

        SHCreateItemFromParsingName(filePath, IntPtr.Zero, IID_IShellItem, out var item);
        item.BindToHandler(IntPtr.Zero, BHID_DataObject, IID_IDataObject, out var dataObject);

        var dropSource = new DropSource();
        // DoDragDrop은 마우스 버튼을 뗄 때까지 블로킹하는 모달 드래그 루프다.
        DoDragDrop(dataObject, dropSource, DROPEFFECT_COPY | DROPEFFECT_LINK, out _);
    }

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    [DllImport("ole32.dll")]
    private static extern int DoDragDrop(
        ComTypes.IDataObject pDataObj, IDropSource pDropSource, int dwOKEffects, out int pdwEffect);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        // BindToHandler가 첫 메서드라 이것만 선언해도 호출 가능하다.
        [PreserveSig]
        int BindToHandler(IntPtr pbc, in Guid bhid, in Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out ComTypes.IDataObject ppv);
    }

    [ComImport]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool fEscapePressed, uint grfKeyState);

        [PreserveSig]
        int GiveFeedback(uint dwEffect);
    }

    /// <summary>표준 드래그 동작: ESC=취소, 버튼 떼면 드롭, 그 외 진행.</summary>
    private sealed class DropSource : IDropSource
    {
        private const int S_OK = 0;
        private const int DRAGDROP_S_DROP = 0x00040100;
        private const int DRAGDROP_S_CANCEL = 0x00040101;
        private const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;
        private const uint MK_LBUTTON = 0x0001;

        public int QueryContinueDrag(bool fEscapePressed, uint grfKeyState)
        {
            if (fEscapePressed)
                return DRAGDROP_S_CANCEL;
            if ((grfKeyState & MK_LBUTTON) == 0)
                return DRAGDROP_S_DROP;
            return S_OK;
        }

        public int GiveFeedback(uint dwEffect) => DRAGDROP_S_USEDEFAULTCURSORS;
    }
}
