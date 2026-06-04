using Microsoft.UI.Xaml.Markup;
using WorkGroup.App.Services;

namespace WorkGroup.App.Markup;

/// <summary>
/// XAML에서 리소스 키를 현재 언어 문자열로 치환하는 마크업 익스텐션(plan.md D1).
/// 사용: <c>Text="{loc:Localize Key=Settings_Title}"</c>. 재시작 기반 언어 전환(D2)이라 1회 평가로 충분하다.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed partial class LocalizeExtension : MarkupExtension
{
    /// <summary>조회할 리소스 키.</summary>
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue()
        => LocalizationService.Current?.Get(Key) ?? Key;
}
