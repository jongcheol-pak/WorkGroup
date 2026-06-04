using Microsoft.Windows.AppLifecycle;
using Windows.Globalization;
using Windows.Storage;

namespace WorkGroup.App.Services;

/// <summary>
/// 앱 표시 언어를 영속·적용한다(plan.md T2/D3). 영속은 LocalSettings "AppLanguage" 키,
/// 적용은 (1) <see cref="ApplicationLanguages.PrimaryLanguageOverride"/>(매니페스트/셸 ms-resource용)와
/// (2) <see cref="LocalizationService"/>의 언어 한정자(인앱 문구용)에 동시 반영한다.
/// 전환은 재시작 방식이므로(D2) 변경 시 저장 후 <see cref="AppInstance.Restart"/>로 적용한다.
/// </summary>
public sealed class LanguageService
{
    private const string Key = "AppLanguage";

    /// <summary>"시스템 언어" 선택값(저장/표시용 식별자).</summary>
    public const string System = "System";

    private readonly LocalizationService _localization;

    public LanguageService(LocalizationService localization) => _localization = localization;

    /// <summary>설정 화면 ComboBox 순서와 일치하는 지원 언어 목록(0번=시스템 언어).</summary>
    public static IReadOnlyList<string> SupportedChoices { get; } =
        [System, "ko-KR", "en-US", "ja-JP", "zh-Hans"];

    /// <summary>저장된 언어 선택값을 반환한다(없거나 미지원이면 "System").</summary>
    public string Read()
    {
        try
        {
            var saved = ApplicationData.Current.LocalSettings.Values[Key] as string;
            return saved is not null && SupportedChoices.Contains(saved) ? saved : System;
        }
        catch
        {
            // 비패키지 실행 등으로 LocalSettings 접근 실패 시 시스템 언어.
            return System;
        }
    }

    /// <summary>앱 시작 시 저장된 언어를 PrimaryLanguageOverride와 LocalizationService에 적용한다(창 생성 이전 호출).</summary>
    public void ApplyOnStartup() => Apply(Read());

    /// <summary>언어를 저장하고 앱을 재시작해 적용한다. 현재 선택과 같으면 아무것도 하지 않는다.</summary>
    public void ChangeAndRestart(string choice)
    {
        var normalized = SupportedChoices.Contains(choice) ? choice : System;
        if (normalized == Read()) return; // 동일 선택은 불필요한 재시작 방지.

        Save(normalized);
        try
        {
            // 무인자 재시작 = 일반 실행 경로(메인 창 표시). 시작 시 ApplyOnStartup이 새 언어를 반영.
            AppInstance.Restart(string.Empty);
        }
        catch
        {
            // 비패키지 실행 등으로 재시작 실패 시 저장만 유지(다음 실행에 반영).
        }
    }

    // PrimaryLanguageOverride(시스템이면 빈 문자열)와 인앱 리소스 한정자에 동시 적용.
    private void Apply(string choice)
    {
        var tag = choice == System ? string.Empty : choice;
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = tag;
        }
        catch
        {
            // 일부 환경에서 override 설정 실패는 무시(인앱 문구는 LocalizationService로 별도 반영).
        }
        _localization.SetLanguage(tag);
    }

    private static void Save(string choice)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[Key] = choice;
        }
        catch
        {
            // 접근 실패는 무시한다(메모리상 적용은 됨).
        }
    }
}
