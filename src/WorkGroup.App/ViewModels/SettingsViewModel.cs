using CommunityToolkit.Mvvm.ComponentModel;
using WorkGroup.App.Services;

namespace WorkGroup.App.ViewModels;

/// <summary>
/// 설정 화면 ViewModel(plan.md T3). 로그인 자동 시작 토글, 앱 테마(시스템/라이트/다크) 전환,
/// 표시 언어(시스템/한국어/영어/일본어/중국어) 선택을 제공한다.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly StartupService _startup;
    private readonly ThemeService _theme;
    private readonly LanguageService _language;

    // 초기 로드 중 변경 핸들러의 오발동을 막는다(plan.md M3).
    private bool _suppress;

    public SettingsViewModel(StartupService startup, ThemeService theme, LanguageService language)
    {
        _startup = startup;
        _theme = theme;
        _language = language;
        StatusMessage = string.Empty;
    }

    /// <summary>언어 변경 시 재시작 확인을 View에 위임하는 콜백(코드비하인드가 설정, true=확인).</summary>
    public Func<Task<bool>>? ConfirmRestartAsync { get; set; }

    [ObservableProperty]
    public partial bool AutoStartEnabled { get; set; }

    /// <summary>테마 선택 인덱스(0=시스템, 1=라이트, 2=다크) — RadioButtons.SelectedIndex 바인딩용.</summary>
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    /// <summary>언어 선택 인덱스(LanguageService.SupportedChoices 순서: 0=시스템, 1=ko, 2=en, 3=ja, 4=zh).</summary>
    [ObservableProperty]
    public partial int LanguageIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; }

    /// <summary>상태 메시지 표시 여부(InfoBar.IsOpen 바인딩용).</summary>
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>페이지 진입 시 현재 자동 시작·테마·언어 상태를 로드한다(suppress로 핸들러 억제 — plan.md M3).</summary>
    public async Task LoadAsync()
    {
        _suppress = true;
        AutoStartEnabled = await _startup.IsEnabledAsync();
        ThemeIndex = _theme.Read() switch
        {
            ElementTheme.Light => 1,
            ElementTheme.Dark => 2,
            _ => 0
        };
        LanguageIndex = IndexOfChoice(_language.Read());
        _suppress = false;
    }

    async partial void OnAutoStartEnabledChanged(bool value)
    {
        if (_suppress) return;

        var actual = await _startup.SetEnabledAsync(value);
        if (actual != value)
        {
            // 정책/권한으로 적용이 거부되면 실제 상태로 되돌린다.
            _suppress = true;
            AutoStartEnabled = actual;
            _suppress = false;
            StatusMessage = "자동 시작 설정이 적용되지 않았습니다(정책/권한).";
        }
        else
        {
            StatusMessage = string.Empty;
        }
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_suppress) return;

        _theme.Set(value switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default
        });
    }

    async partial void OnLanguageIndexChanged(int value)
    {
        if (_suppress) return;
        if (value < 0 || value >= LanguageService.SupportedChoices.Count) return;

        var choice = LanguageService.SupportedChoices[value];
        if (choice == _language.Read()) return; // 현재와 동일하면 무시.

        System.Diagnostics.Debug.Assert(ConfirmRestartAsync is not null,
            "ConfirmRestartAsync 콜백이 설정되지 않았습니다(코드비하인드 미연결).");

        // await 이전에 게이트를 잠가 다이얼로그 표시 중 재진입(중복 다이얼로그)을 막는다.
        _suppress = true;
        try
        {
            // 재시작 확인을 View에 위임. 확인 시 저장 후 재시작(이후 코드는 도달하지 않음).
            var confirmed = ConfirmRestartAsync is not null && await ConfirmRestartAsync();
            if (confirmed)
                _language.ChangeAndRestart(choice);
            else
                LanguageIndex = IndexOfChoice(_language.Read()); // 취소 → 이전 선택으로 복원.
        }
        finally
        {
            _suppress = false;
        }
    }

    // 저장된 선택값을 ComboBox 인덱스로 변환(미일치 시 0=시스템).
    private static int IndexOfChoice(string choice)
    {
        var choices = LanguageService.SupportedChoices;
        for (var i = 0; i < choices.Count; i++)
            if (choices[i] == choice) return i;
        return 0;
    }
}
