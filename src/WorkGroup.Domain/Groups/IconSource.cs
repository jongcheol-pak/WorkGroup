namespace WorkGroup.Domain.Groups;

/// <summary>그룹 아이콘의 출처 종류(plan.md D9 — 세 가지 모두 지원).</summary>
public enum IconSourceKind
{
    /// <summary>앱이 제공하는 내장 아이콘 세트 중 하나.</summary>
    BuiltIn,

    /// <summary>그룹에 속한 멤버 앱의 아이콘을 사용.</summary>
    MemberApp,

    /// <summary>사용자가 지정한 이미지/.ico 파일.</summary>
    CustomImage
}

/// <summary>
/// 그룹을 작업 표시줄에 핀할 때 사용할 아이콘의 출처.
/// <see cref="Value"/>의 의미는 <see cref="Kind"/>에 따라 달라진다:
/// BuiltIn=내장 아이콘 식별자, MemberApp=멤버 앱의 LaunchTarget, CustomImage=이미지 파일 경로.
/// </summary>
public sealed record IconSource(IconSourceKind Kind, string Value)
{
    /// <summary>기본 내장 아이콘.</summary>
    public static IconSource DefaultBuiltIn { get; } = new(IconSourceKind.BuiltIn, "default");

    public static IconSource BuiltIn(string iconId) => new(IconSourceKind.BuiltIn, iconId);
    public static IconSource FromMemberApp(string launchTarget) => new(IconSourceKind.MemberApp, launchTarget);
    public static IconSource FromCustomImage(string filePath) => new(IconSourceKind.CustomImage, filePath);
}
