using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Application.Icons;

/// <summary>
/// 그룹 아이콘(.ico) 생성 추상화. 내장 세트/멤버 앱 아이콘/사용자 이미지의 세 소스를 지원한다(plan.md D9).
/// 실패 시 기본 내장 아이콘으로 대체한다(plan.md T5 Edge Cases).
/// </summary>
public interface IIconService
{
    /// <summary>
    /// 그룹의 아이콘 소스로부터 .ico 파일을 생성하고 그 경로를 반환한다.
    /// 출력 경로는 <paramref name="outputDirectory"/>의 <c>{groupId}.ico</c>.
    /// </summary>
    /// <param name="members">MemberApp 소스일 때 대상 앱을 찾기 위한 그룹 멤버 목록.</param>
    Task<Result<string>> CreateGroupIconAsync(
        GroupId groupId,
        IconSource source,
        IReadOnlyList<AppEntry> members,
        string outputDirectory,
        CancellationToken cancellationToken = default);
}
