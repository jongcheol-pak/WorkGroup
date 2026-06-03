namespace WorkGroup.Application.Folders;

/// <summary>
/// 폴더 한 단계의 파일/하위폴더를 열거하는 추상화. 구현은 Infrastructure(파일시스템).
/// 예외를 던지지 않고 <see cref="DirectoryBrowseStatus"/>로 상태를 표현한다.
/// </summary>
public interface IDirectoryBrowser
{
    DirectoryListing Browse(string path, bool showHidden);
}

/// <summary>폴더 열거 결과 상태.</summary>
public enum DirectoryBrowseStatus
{
    Ok,
    NotFound,
    AccessDenied,
    Empty
}

/// <summary>폴더 내 항목(파일 또는 하위폴더) 하나.</summary>
public sealed record DirectoryEntryInfo(string Name, string FullPath, bool IsDirectory);

/// <summary>폴더 열거 결과(파일 목록 + 폴더 목록 + 상태).</summary>
public sealed record DirectoryListing(
    IReadOnlyList<DirectoryEntryInfo> Files,
    IReadOnlyList<DirectoryEntryInfo> Folders,
    DirectoryBrowseStatus Status);
