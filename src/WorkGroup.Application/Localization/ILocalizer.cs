namespace WorkGroup.Application.Localization;

/// <summary>
/// 리소스 키를 현재 언어 문자열로 변환하는 추상화(plan.md D5).
/// 구현은 App 레이어(LocalizationService)에 두고, 인프라/도메인은 이 인터페이스에만 의존한다.
/// </summary>
public interface ILocalizer
{
    /// <summary>키에 해당하는 현재 언어 문자열을 반환한다. 없으면 키 자체를 반환한다.</summary>
    string Get(string key);

    /// <summary>키 문자열을 포맷 템플릿으로 보고 인자를 채워 반환한다(string.Format).</summary>
    string Get(string key, params object[] args);
}
