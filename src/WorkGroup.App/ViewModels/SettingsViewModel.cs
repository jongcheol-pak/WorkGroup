using CommunityToolkit.Mvvm.ComponentModel;
using WorkGroup.App.Services;

namespace WorkGroup.App.ViewModels;

/// <summary>
/// 설정 화면 ViewModel(plan.md T3). 로그인 자동 시작 토글과 앱 테마(시스템/라이트/다크) 전환을 제공한다.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly StartupService _startup;
    private readonly ThemeService _theme;

    // 초기 로드 중 변경 핸들러의 오발동을 막는다(plan.md M3).
    private bool _suppress;

    public SettingsViewModel(StartupService startup, ThemeService theme)
    {
        _startup = startup;
        _theme = theme;
        StatusMessage = string.Empty;
    }

    [ObservableProperty]
    public partial bool AutoStartEnabled { get; set; }

    /// <summary>테마 선택 인덱스(0=시스템, 1=라이트, 2=다크) — RadioButtons.SelectedIndex 바인딩용.</summary>
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string StatusMessage { get; set; }

    /// <summary>상태 메시지 표시 여부(InfoBar.IsOpen 바인딩용).</summary>
    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>페이지 진입 시 현재 자동 시작·테마 상태를 로드한다(suppress로 핸들러 억제 — plan.md M3).</summary>
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
}
