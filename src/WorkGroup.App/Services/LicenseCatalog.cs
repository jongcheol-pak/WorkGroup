namespace WorkGroup.App.Services;

/// <summary>정보 페이지에 표시할 라이선스 항목(plan.md DU4 — 이름+종류+링크).</summary>
public sealed record LicenseInfo(string Name, string License, string Url)
{
    /// <summary>HyperlinkButton.NavigateUri 바인딩용(문자열 → Uri).</summary>
    public Uri Link => new(Url);
}

/// <summary>
/// 배포 런타임에 포함되는 서드파티 의존성의 라이선스 목록(plan.md DU4).
/// 테스트 전용 패키지(xUnit 등)는 배포되지 않으므로 제외한다.
/// Microsoft 런타임 패키지는 MIT가 아닌 독점(Microsoft Software License)으로 정확히 표기한다.
/// </summary>
public static class LicenseCatalog
{
    public static IReadOnlyList<LicenseInfo> Items { get; } = new[]
    {
        new LicenseInfo("CommunityToolkit.Mvvm", "MIT License", "https://github.com/CommunityToolkit/dotnet"),
        new LicenseInfo("Microsoft.Extensions.DependencyInjection", "MIT License", "https://github.com/dotnet/runtime"),
        // Microsoft.Extensions.Logging.Abstractions도 같은 리포지토리·동일 MIT라 Logging 항목으로 묶어 표기.
        new LicenseInfo("Microsoft.Extensions.Logging", "MIT License", "https://github.com/dotnet/runtime"),
        new LicenseInfo("Microsoft.Windows.CsWin32", "MIT License", "https://github.com/microsoft/CsWin32"),
        new LicenseInfo("Microsoft.WindowsAppSDK", "Microsoft Software License", "https://github.com/microsoft/WindowsAppSDK"),
        new LicenseInfo("Microsoft.Web.WebView2", "Microsoft Software License", "https://developer.microsoft.com/microsoft-edge/webview2/"),
        new LicenseInfo("Microsoft.Windows.SDK.BuildTools", "Microsoft Software License", "https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools"),
    };
}
