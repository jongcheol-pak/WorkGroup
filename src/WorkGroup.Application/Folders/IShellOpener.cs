namespace WorkGroup.Application.Folders;

/// <summary>
/// 폴더/파일을 셸 기본 동작(탐색기/기본 앱)으로 여는 추상화. 구현은 Infrastructure.
/// </summary>
public interface IShellOpener
{
    /// <summary>경로를 셸 기본 동작으로 연다. 실패는 내부에서 흡수한다.</summary>
    void Open(string path);
}
