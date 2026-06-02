using System.Reflection;
using Windows.ApplicationModel;
using WorkGroup.App.Services;

namespace WorkGroup.App.ViewModels;

/// <summary>정보 화면 ViewModel(plan.md T4). 앱 이름·버전과 오픈소스 라이선스 목록을 제공한다.</summary>
public sealed class AboutViewModel
{
    public AboutViewModel() => Version = ReadVersion();

    public string AppName => "WorkGroup";

    /// <summary>패키지 버전(Major.Minor.Build.Revision). 비패키지 실행 시 어셈블리 버전 폴백.</summary>
    public string Version { get; }

    public IReadOnlyList<LicenseInfo> Licenses => LicenseCatalog.Items;

    private static string ReadVersion()
    {
        try
        {
            var v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch
        {
            // 비패키지 실행 등으로 Package.Current 접근 실패 시 어셈블리 버전으로 대체.
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
        }
    }
}
