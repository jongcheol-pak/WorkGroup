using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using WorkGroup.Application.Inventory;
using WorkGroup.Application.Localization;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Infrastructure.Inventory;

/// <summary>
/// 현재 사용자 기준 설치 앱을 수집한다(plan.md D5/D6).
/// - 패키지(Store/UWP): PackageManager로 열거, 실행 대상 = AUMID.
/// - Win32: 시작 메뉴의 .lnk 바로가기 열거, 실행 대상 = .lnk 경로(셸 실행).
/// 표시명(대소문자 무시) 기준으로 중복을 제거하며 패키지 항목을 우선한다.
/// </summary>
public sealed class InstalledAppInventory : IAppInventory
{
    private readonly ILogger<InstalledAppInventory> _logger;
    private readonly ILocalizer _localizer;

    public InstalledAppInventory(ILogger<InstalledAppInventory>? logger = null, ILocalizer? localizer = null)
    {
        _logger = logger ?? NullLogger<InstalledAppInventory>.Instance;
        _localizer = localizer ?? NullLocalizer.Instance;
    }

    public async Task<IReadOnlyList<AppEntry>> GetInstalledAppsAsync(CancellationToken cancellationToken = default)
    {
        var packaged = await GetPackagedAppsAsync(cancellationToken).ConfigureAwait(false);
        var win32 = GetStartMenuApps(cancellationToken);
        return MergeApps(packaged, win32);
    }

    /// <summary>
    /// 두 소스를 합쳐 표시명(대소문자 무시) 기준으로 중복을 제거하고 이름순 정렬한다(패키지 우선).
    /// 순수 로직이므로 단위 테스트 대상으로 분리한다.
    /// </summary>
    internal static IReadOnlyList<AppEntry> MergeApps(IReadOnlyList<AppEntry> packaged, IReadOnlyList<AppEntry> win32)
    {
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AppEntry>();

        // 패키지 앱을 먼저 넣어 동일 표시명 충돌 시 패키지 항목을 유지한다.
        foreach (var app in packaged.Concat(win32))
        {
            if (seenNames.Add(app.DisplayName))
                result.Add(app);
        }

        return result
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public Result<AppEntry> CreateManualEntry(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return Result<AppEntry>.Fail(_localizer.Get("Infra_Inventory_EmptyPath"));
        if (!File.Exists(filePath))
            return Result<AppEntry>.Fail(_localizer.Get("Infra_Inventory_FileNotFound"));

        var ext = Path.GetExtension(filePath);
        if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            && !ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return Result<AppEntry>.Fail(_localizer.Get("Infra_Inventory_InvalidType"));
        }

        var name = Path.GetFileNameWithoutExtension(filePath);
        return Result<AppEntry>.Ok(new AppEntry(name, filePath, AppKind.Win32, filePath));
    }

    // ----- 패키지(Store/UWP) -----

    private Task<IReadOnlyList<AppEntry>> GetPackagedAppsAsync(CancellationToken cancellationToken)
    {
        // 동기 WinRT 호출이지만 한 소스의 실패가 전체를 막지 않도록 분리해 감싼다.
        return Task.Run<IReadOnlyList<AppEntry>>(() =>
        {
            var apps = new List<AppEntry>();
            try
            {
                var manager = new PackageManager();
                // 빈 문자열 = 현재 사용자(관리자 권한 불필요 — D6).
                foreach (var package in manager.FindPackagesForUser(string.Empty))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (package.IsFramework || package.IsResourcePackage)
                        continue;

                    AppEntry? entry = TryMapPackage(package);
                    if (entry is not null)
                        apps.Add(entry);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 권한 없음 등 → 이 소스는 건너뛰고 나머지를 반환(plan.md T4 Edge Cases).
                _logger.LogWarning(ex, "패키지 앱 열거에 실패했습니다. 해당 소스를 건너뜁니다.");
            }

            return apps;
        }, cancellationToken);
    }

    private AppEntry? TryMapPackage(Package package)
    {
        // GetAppListEntries()는 Windows 10.0.19041.0+ 에서만 지원된다(앱 대상은 Windows 11).
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return null;

        try
        {
            var entries = package.GetAppListEntries();
            if (entries.Count == 0)
                return null;

            // 패키지의 첫 번째 앱(주 진입점)을 대표로 사용한다.
            var appEntry = entries[0];
            var name = appEntry.DisplayInfo?.DisplayName;
            var aumid = appEntry.AppUserModelId;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(aumid))
                return null;

            return new AppEntry(name, aumid, AppKind.Packaged, ResolveLogoPath(package));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "패키지 매핑 실패: {Package}", package.Id?.FamilyName);
            return null;
        }
    }

    private string? ResolveLogoPath(Package package)
    {
        // 패키지 로고가 실제 파일이면 경로를 반환(아이콘 추출용). 없으면 null(IconService가 기본으로 대체).
        try
        {
            var logo = package.Logo;
            if (logo is not null && logo.IsFile)
            {
                var path = logo.LocalPath;
                if (File.Exists(path))
                    return path;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "패키지 로고 경로 접근 실패: {Package}", package.Id?.FamilyName);
        }

        return null;
    }

    // ----- Win32(시작 메뉴 바로가기) -----

    private IReadOnlyList<AppEntry> GetStartMenuApps(CancellationToken cancellationToken)
    {
        var apps = new List<AppEntry>();
        foreach (var root in StartMenuRoots())
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = Path.GetFileNameWithoutExtension(lnk);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // 실행 대상·아이콘 모두 .lnk 경로(셸이 처리). 제거(uninstall) 관련 항목은 제외.
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                        continue;

                    apps.Add(new AppEntry(name, lnk, AppKind.Win32, lnk));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "시작 메뉴 열거 실패: {Root}", root);
            }
        }

        return apps;
    }

    private static IEnumerable<string> StartMenuRoots()
    {
        // 공통(전체 사용자 공유) + 현재 사용자 시작 메뉴의 Programs 폴더.
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    }
}
