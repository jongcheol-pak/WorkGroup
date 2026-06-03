using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using WorkGroup.App.Services;
using WorkGroup.Domain.Folders;

namespace WorkGroup.App.Views;

/// <summary>
/// 폴더 팝업 설정 다이얼로그. 열 개수(1~5)/하위폴더 깊이(1~5)/숨김 표시를 편집한다.
/// 저장 시 도메인 FolderPopupSettings.Create로 클램프해 LocalSettings에 저장한다. 다음 좌클릭 팝업부터 반영.
/// </summary>
public sealed partial class FolderPopupSettingsDialog : ContentDialog
{
    private readonly FolderPopupSettingsService _service;

    public FolderPopupSettingsDialog()
    {
        InitializeComponent();
        _service = App.Services.GetRequiredService<FolderPopupSettingsService>();

        // ComboBox는 0-based, 설정값은 1-based.
        var settings = _service.Read();
        ColumnCountCombo.SelectedIndex = settings.ColumnCount - 1;
        DepthCombo.SelectedIndex = settings.SubfolderDepth - 1;
        HiddenToggle.IsOn = settings.ShowHiddenItems;
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // SelectedIndex(-1 포함)+1을 도메인 Create가 1~5로 클램프한다.
        var settings = FolderPopupSettings.Create(
            ColumnCountCombo.SelectedIndex + 1,
            DepthCombo.SelectedIndex + 1,
            HiddenToggle.IsOn);
        _service.Save(settings);
    }
}
