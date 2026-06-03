namespace WorkGroup.Domain.Folders;

/// <summary>
/// 폴더 팝업 표시 설정. 열 개수와 하위폴더 탐색 깊이는 1~5로 제한한다.
/// </summary>
public sealed record FolderPopupSettings
{
    public const int MinColumnCount = 1;
    public const int MaxColumnCount = 5;
    public const int MinSubfolderDepth = 1;
    public const int MaxSubfolderDepth = 5;

    private FolderPopupSettings(int columnCount, int subfolderDepth, bool showHiddenItems)
    {
        ColumnCount = columnCount;
        SubfolderDepth = subfolderDepth;
        ShowHiddenItems = showHiddenItems;
    }

    /// <summary>팝업 폴더 목록의 열 개수(1=세로 목록, 2~5=그리드).</summary>
    public int ColumnCount { get; }

    /// <summary>폴더 호버 시 표시할 하위 폴더 탐색 깊이.</summary>
    public int SubfolderDepth { get; }

    /// <summary>폴더 내용에 숨김 파일/폴더를 포함할지 여부.</summary>
    public bool ShowHiddenItems { get; }

    /// <summary>기본 설정(열 1개, 하위폴더 깊이 2, 숨김 항목 미표시).</summary>
    public static FolderPopupSettings Default { get; } = new(1, 2, false);

    /// <summary>설정을 생성한다. 열 개수·깊이는 1~5로 클램프한다.</summary>
    public static FolderPopupSettings Create(int columnCount, int subfolderDepth, bool showHiddenItems) =>
        new(
            Math.Clamp(columnCount, MinColumnCount, MaxColumnCount),
            Math.Clamp(subfolderDepth, MinSubfolderDepth, MaxSubfolderDepth),
            showHiddenItems);
}
