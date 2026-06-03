using WorkGroup.Domain.Common;

namespace WorkGroup.Domain.Folders;

/// <summary>
/// 트레이 좌클릭 팝업에 표시할 등록 폴더 바로가기.
/// 불변식: 이름과 경로는 비어 있을 수 없다. 편집은 같은 Id로 새 인스턴스를 만들어 저장한다.
/// </summary>
public sealed record FolderShortcut
{
    private FolderShortcut(int id, string name, string path)
    {
        Id = id;
        Name = name;
        Path = path;
    }

    public int Id { get; }
    public string Name { get; }
    public string Path { get; }

    /// <summary>폴더 바로가기를 생성한다. 이름·경로가 비어 있으면 실패를 반환한다.</summary>
    public static Result<FolderShortcut> Create(int id, string name, string path)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<FolderShortcut>.Fail("폴더 이름은 필수입니다.");
        if (string.IsNullOrWhiteSpace(path))
            return Result<FolderShortcut>.Fail("폴더 경로는 필수입니다.");

        return Result<FolderShortcut>.Ok(new FolderShortcut(id, name.Trim(), path.Trim()));
    }
}
