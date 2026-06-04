using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WorkGroup.App.Services;
using WorkGroup.Application.Folders;

namespace WorkGroup.App.Views;

/// <summary>
/// 폴더 바로가기 추가/수정 다이얼로그. 이름 입력 + 폴더 선택(FolderPicker), 확인 시 검증·저장.
/// 저장 실패(빈 값/경로 중복/없는 항목) 시 닫힘을 보류하고 메시지를 표시한다.
/// </summary>
public sealed partial class FolderEditDialog : ContentDialog
{
    private readonly IFolderShortcutRepository _repository;
    private int? _editingId;

    public FolderEditDialog()
    {
        InitializeComponent();
        _repository = App.Services.GetRequiredService<IFolderShortcutRepository>();
    }

    /// <summary>신규(null) 또는 편집 대상(Id/이름/경로)을 지정한다(ShowAsync 전 호출).</summary>
    public void Configure(int? id, string? name, string? path)
    {
        _editingId = id;
        Title = LocalizationService.Current?.Get(id is null ? "FolderEdit_AddTitle" : "FolderEdit_EditTitle") ?? string.Empty;
        NameTextBox.Text = name ?? string.Empty;
        PathTextBlock.Text = path ?? string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private async void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        // WinUI 3에서 Picker는 소유 창 HWND 초기화가 필요하다.
        if (App.MainWindow is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            PathTextBlock.Text = folder.Path;
            // 이름이 비어 있으면 폴더명으로 자동 채움.
            if (string.IsNullOrWhiteSpace(NameTextBox.Text))
                NameTextBox.Text = folder.Name;
        }
    }

    private async void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // 검증·저장이 끝날 때까지 닫힘을 보류한다.
        var deferral = args.GetDeferral();
        try
        {
            var name = NameTextBox.Text.Trim();
            var path = PathTextBlock.Text.Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            {
                ShowError(LocalizationService.Current?.Get("FolderEdit_ValidationRequired") ?? string.Empty);
                args.Cancel = true;
                return;
            }

            var result = _editingId is null
                ? await _repository.AddAsync(name, path)
                : await _repository.UpdateAsync(_editingId.Value, name, path);

            if (result.IsFailure)
            {
                ShowError(result.Error!);
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
