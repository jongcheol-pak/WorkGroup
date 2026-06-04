namespace WorkGroup.Application.Localization;

/// <summary>
/// 로컬라이저가 주입되지 않았을 때의 폴백(plan.md D5). 키 문자열을 그대로 반환한다.
/// 인프라 서비스의 <c>ILocalizer? = null</c> 선택 인자 폴백으로 쓰며, DI 경로에서는 실제 구현이 주입된다.
/// (기존 <c>ILogger? → NullLogger</c> 패턴과 동형.)
/// </summary>
public sealed class NullLocalizer : ILocalizer
{
    public static readonly NullLocalizer Instance = new();

    private NullLocalizer() { }

    public string Get(string key) => key ?? string.Empty;

    public string Get(string key, params object[] args) => key ?? string.Empty;
}
