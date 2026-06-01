using System.Runtime.InteropServices;

namespace WorkGroup.Infrastructure.Shortcuts;

/// <summary>
/// IShellLink COM으로 .lnk 바로가기를 생성한다(plan.md T2 C1 / T7 셸 서비스 핵심).
/// 타깃은 실행 별칭(WorkGroupSpike.exe)이고 인자/아이콘을 지정한다.
/// </summary>
public sealed class ShortcutWriter
{
    /// <summary>지정 경로에 .lnk를 생성/덮어쓴다.</summary>
    public void Create(string lnkPath, string targetPath, string? arguments = null, string? iconPath = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(lnkPath))
            throw new ArgumentException("바로가기 경로가 비어 있습니다.", nameof(lnkPath));
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("타깃 경로가 비어 있습니다.", nameof(targetPath));

        var directory = Path.GetDirectoryName(lnkPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(targetPath);
            if (!string.IsNullOrEmpty(arguments))
                link.SetArguments(arguments);
            if (!string.IsNullOrEmpty(iconPath))
                link.SetIconLocation(iconPath, 0);
            if (!string.IsNullOrEmpty(description))
                link.SetDescription(description);

            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }
}

// ----- IShellLink / IPersistFile COM 정의(메서드 순서가 vtable과 일치해야 함) -----

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink
{
}

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}

[ComImport]
[Guid("0000010b-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);
    [PreserveSig]
    int IsDirty();
    void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
    void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
    void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
}
