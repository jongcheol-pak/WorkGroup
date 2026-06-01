namespace WorkGroup.Domain.Groups;

/// <summary>설치 앱의 종류(실행 방식이 달라진다 — plan.md T11).</summary>
public enum AppKind
{
    /// <summary>Win32 데스크톱 앱. LaunchTarget = 실행 파일 경로.</summary>
    Win32,

    /// <summary>Store/UWP 패키지 앱. LaunchTarget = AUMID.</summary>
    Packaged
}

/// <summary>
/// 그룹에 속한 하나의 앱. 동일성은 <see cref="LaunchTarget"/>(대소문자 무시)으로 판정한다.
/// </summary>
public sealed record AppEntry
{
    public AppEntry(string displayName, string launchTarget, AppKind kind, string? iconLocation = null)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("앱 표시 이름은 비어 있을 수 없습니다.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(launchTarget))
            throw new ArgumentException("앱 실행 대상은 비어 있을 수 없습니다.", nameof(launchTarget));

        DisplayName = displayName.Trim();
        LaunchTarget = launchTarget.Trim();
        Kind = kind;
        IconLocation = string.IsNullOrWhiteSpace(iconLocation) ? null : iconLocation.Trim();
    }

    /// <summary>팝업·목록에 표시할 이름.</summary>
    public string DisplayName { get; }

    /// <summary>실행 대상(Win32: 실행 파일 경로, Packaged: AUMID).</summary>
    public string LaunchTarget { get; }

    public AppKind Kind { get; }

    /// <summary>원본 아이콘 위치(실행 파일/로고 경로). 없으면 null.</summary>
    public string? IconLocation { get; }

    /// <summary>실행 대상이 같으면 같은 앱으로 본다(대소문자 무시).</summary>
    public bool SameTarget(string launchTarget) =>
        string.Equals(LaunchTarget, launchTarget?.Trim(), StringComparison.OrdinalIgnoreCase);
}
