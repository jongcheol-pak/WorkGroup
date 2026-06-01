namespace WorkGroup.Domain.Groups;

/// <summary>
/// 작업 그룹 식별자. GUID 문자열("N" 포맷)을 값으로 가지며 .lnk 파일명·AUMID 생성의 기준이 된다(plan.md D3).
/// </summary>
public sealed record GroupId(string Value)
{
    /// <summary>새 그룹 식별자를 생성한다.</summary>
    public static GroupId New() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}
