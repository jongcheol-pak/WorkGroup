using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Application.Inventory;

/// <summary>
/// PC에 설치된 앱 목록을 수집하는 추상화(plan.md D5/D6 — 현재 사용자 범위, Win32 + Store/UWP).
/// 반환 항목은 그룹에 추가 가능한 후보 앱이다.
/// </summary>
public interface IAppInventory
{
    /// <summary>설치된 앱들을 표시명 기준 중복 제거하여 반환한다. 한 소스가 실패해도 나머지는 반환한다.</summary>
    Task<IReadOnlyList<AppEntry>> GetInstalledAppsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 사용자가 직접 고른 실행 파일(.exe/.lnk)로부터 앱 항목을 만든다(plan.md T4 — 수동 추가).
    /// 파일이 없거나 지원하지 않는 형식이면 실패를 반환한다.
    /// </summary>
    Result<AppEntry> CreateManualEntry(string filePath);
}
