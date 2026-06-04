using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WorkGroup.App.ViewModels;

namespace WorkGroup.App.Views;

/// <summary>
/// 핀 팝업 GridView의 항목 타입에 따라 템플릿을 고른다.
/// <see cref="PopupAddButtonItem"/>은 "+" 버튼 템플릿, 그 외(앱 항목)는 앱 아이콘 템플릿.
/// </summary>
public sealed partial class PopupGridItemTemplateSelector : DataTemplateSelector
{
    /// <summary>앱 아이콘 항목 템플릿.</summary>
    public DataTemplate? AppTemplate { get; set; }

    /// <summary>"+" 추가(그룹 편집) 버튼 항목 템플릿.</summary>
    public DataTemplate? AddButtonTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => item is PopupAddButtonItem ? AddButtonTemplate : AppTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
