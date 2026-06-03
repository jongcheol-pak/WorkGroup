using System.Text.Json;
using Windows.Storage;
using WorkGroup.Domain.Folders;

namespace WorkGroup.App.Services;

/// <summary>
/// 폴더 팝업 설정(열 개수/하위폴더 깊이/숨김 표시)을 LocalSettings에 JSON으로 저장한다.
/// ThemeService와 동일하게 비패키지/접근 실패 시 기본값으로 폴백한다.
/// </summary>
public sealed class FolderPopupSettingsService
{
    private const string Key = "FolderPopupSettings";

    /// <summary>저장된 설정을 읽는다. 없거나 손상/접근 실패 시 기본값을 반환한다(값은 도메인 Create로 클램프).</summary>
    public FolderPopupSettings Read()
    {
        try
        {
            if (ApplicationData.Current.LocalSettings.Values[Key] is string json && !string.IsNullOrWhiteSpace(json))
            {
                var dto = JsonSerializer.Deserialize<SettingsDto>(json);
                if (dto is not null)
                    return FolderPopupSettings.Create(dto.ColumnCount, dto.SubfolderDepth, dto.ShowHiddenItems);
            }
        }
        catch
        {
            // 손상/접근 실패는 기본값으로 폴백.
        }
        return FolderPopupSettings.Default;
    }

    /// <summary>설정을 LocalSettings에 저장한다. 접근 실패는 무시한다.</summary>
    public void Save(FolderPopupSettings settings)
    {
        try
        {
            var dto = new SettingsDto(settings.ColumnCount, settings.SubfolderDepth, settings.ShowHiddenItems);
            ApplicationData.Current.LocalSettings.Values[Key] = JsonSerializer.Serialize(dto);
        }
        catch
        {
            // 접근 실패는 무시(다음 저장에서 재시도).
        }
    }

    // STJ 역직렬화용 DTO(도메인 FolderPopupSettings는 private 생성자라 직접 역직렬화 불가).
    private sealed record SettingsDto(int ColumnCount, int SubfolderDepth, bool ShowHiddenItems);
}
