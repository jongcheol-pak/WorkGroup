using WorkGroup.Application.Folders;

namespace WorkGroup.Infrastructure.Folders;

/// <summary>
/// 폴더 한 단계의 파일/하위폴더를 열거한다. 숨김 필터·이름 정렬을 적용하고,
/// 예외는 던지지 않고 <see cref="DirectoryBrowseStatus"/>로 표현한다.
/// </summary>
public sealed class DirectoryBrowser : IDirectoryBrowser
{
    public DirectoryListing Browse(string path, bool showHidden)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return Empty(DirectoryBrowseStatus.NotFound);

        try
        {
            var dir = new DirectoryInfo(path);

            var files = dir.GetFiles()
                .Where(f => showHidden || (f.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => new DirectoryEntryInfo(f.Name, f.FullName, false))
                .ToList();

            var folders = dir.GetDirectories()
                .Where(d => showHidden || (d.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new DirectoryEntryInfo(d.Name, d.FullName, true))
                .ToList();

            var status = files.Count == 0 && folders.Count == 0
                ? DirectoryBrowseStatus.Empty
                : DirectoryBrowseStatus.Ok;

            return new DirectoryListing(files, folders, status);
        }
        catch (UnauthorizedAccessException)
        {
            return Empty(DirectoryBrowseStatus.AccessDenied);
        }
        catch (IOException)
        {
            // 열거 도중 폴더가 사라지거나 접근 불가한 경우 등.
            return Empty(DirectoryBrowseStatus.NotFound);
        }
    }

    private static DirectoryListing Empty(DirectoryBrowseStatus status) =>
        new(Array.Empty<DirectoryEntryInfo>(), Array.Empty<DirectoryEntryInfo>(), status);
}
