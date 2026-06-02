using Microsoft.UI.Xaml.Media;

namespace WorkGroup.App.ViewModels;

/// <summary>리소스 아이콘 그리드 항목(plan.md T4). ms-appx URI + UI 스레드에서 미리 만든 이미지.</summary>
public sealed class ResourceIconItem
{
    public ResourceIconItem(string uri, ImageSource image)
    {
        Uri = uri;
        Image = image;
    }

    /// <summary>아이콘의 ms-appx URI(IconSource.Value로 저장됨).</summary>
    public string Uri { get; }

    public ImageSource Image { get; }
}
