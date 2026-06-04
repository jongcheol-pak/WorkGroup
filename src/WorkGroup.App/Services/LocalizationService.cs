using Microsoft.Windows.ApplicationModel.Resources;
using WorkGroup.Application.Localization;

namespace WorkGroup.App.Services;

/// <summary>
/// MRT Core 리소스(<c>Strings/{lang}/Resources.resw</c>)를 현재 언어로 조회한다(plan.md D1/D3).
/// 언어 한정자를 직접 구성한 <see cref="ResourceContext"/>로 명시 제어하므로,
/// XAML 내부 컨텍스트의 PrimaryLanguageOverride 반영 여부에 의존하지 않는다.
/// 마크업 익스텐션·코드비하인드·트레이는 static <see cref="Current"/>로, ViewModel/인프라는 DI(ILocalizer)로 접근한다.
/// </summary>
public sealed class LocalizationService : ILocalizer
{
    // Resources.resw 키가 속하는 리소스 맵 이름.
    private const string MapName = "Resources";

    private readonly ResourceManager _manager;
    private readonly ResourceContext _context;

    /// <summary>마크업/코드비하인드/트레이용 전역 접근자(App 생성자에서 DI 인스턴스로 설정).</summary>
    public static LocalizationService? Current { get; set; }

    public LocalizationService()
    {
        _manager = new ResourceManager();
        _context = _manager.CreateResourceContext();
    }

    /// <summary>
    /// 조회 언어를 설정한다. <paramref name="bcp47Tag"/>가 비면(시스템 언어) 한정자를 제거해 기본(시스템) 언어를 따른다.
    /// </summary>
    public void SetLanguage(string? bcp47Tag)
    {
        try
        {
            if (string.IsNullOrEmpty(bcp47Tag))
                _context.QualifierValues.Remove("Language");
            else
                _context.QualifierValues["Language"] = bcp47Tag;
        }
        catch (Exception ex)
        {
            // 한정자 설정 실패 시 기본 컨텍스트로 동작하되, 원인을 진단 로그로 남긴다.
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] SetLanguage 실패: {ex.Message}");
        }
    }

    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        try
        {
            var candidate = _manager.MainResourceMap.TryGetValue($"{MapName}/{key}", _context);
            return candidate?.ValueAsString ?? key;
        }
        catch (Exception ex)
        {
            // 비패키지 실행 등으로 리소스 접근 실패 시 키 자체를 폴백으로 반환(크래시 금지).
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] Get('{key}') 실패: {ex.Message}");
            return key;
        }
    }

    public string Get(string key, params object[] args)
    {
        var format = Get(key);
        try
        {
            return string.Format(format, args);
        }
        catch (FormatException)
        {
            // 자리표시자 불일치 시 원문 반환.
            return format;
        }
    }
}
