using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkGroup.Application.Inventory;
using WorkGroup.Application.Localization;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Infrastructure.Inventory;

/// <summary>
/// 현재 사용자 기준 설치 앱을 수집한다(plan.md D5/D6).
/// - 패키지(Store/UWP): shell:AppsFolder를 Shell.Application COM으로 열거(AUMID 항목만), 실행 대상 = AUMID. packageQuery 제한 기능 불필요.
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
        // shell:AppsFolder COM 열거는 동기이므로 오프로드한다. 한 소스 실패가 전체를 막지 않도록 분리해 감싼다.
        return Task.Run<IReadOnlyList<AppEntry>>(() => GetPackagedAppsFromShellFolder(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// shell:AppsFolder(가상 셸 폴더)를 Shell.Application COM으로 열거해 패키지 앱만 수집한다.
    /// 항목의 Path가 AUMID(PackageFamilyName!AppId) 형식이면 패키지로 본다. Win32(.exe 경로 등)는 .lnk 소스가 담당하므로 제외.
    /// </summary>
    private IReadOnlyList<AppEntry> GetPackagedAppsFromShellFolder(CancellationToken cancellationToken)
    {
        var apps = new List<AppEntry>();
        dynamic? shell = null;
        dynamic? folder = null;
        dynamic? items = null;
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return apps;

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return apps;

            folder = shell.NameSpace("shell:AppsFolder");
            if (folder is null)
                return apps;

            // Items()는 별도 FolderItems COM 객체이므로 변수로 보관해 finally에서 해제한다.
            items = folder.Items();
            foreach (dynamic item in items)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string? name = item.Name;
                    string? path = item.Path;
                    // 아이콘은 소비자가 shell:AppsFolder\{AUMID}로 직접 렌더하므로 IconLocation은 생략한다.
                    if (!string.IsNullOrWhiteSpace(name) && IsPackagedAumid(path))
                        apps.Add(new AppEntry(name, path!, AppKind.Packaged));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 항목 단위 실패는 노이즈가 크므로 Debug 수준(기존 TryMapPackage와 동일). 전체 실패만 Warning.
                    _logger.LogDebug(ex, "shell:AppsFolder 항목 처리 실패");
                }
                finally
                {
                    Marshal.ReleaseComObject(item);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 취소는 부분 결과를 버리고 전파한다(기존 PackageManager 경로와 동일 — 취소=취소).
            throw;
        }
        catch (Exception ex)
        {
            // 권한/COM 실패 등 → 이 소스는 건너뛰고 나머지를 반환(plan.md T1 Edge Cases).
            _logger.LogWarning(ex, "패키지 앱(shell:AppsFolder) 열거에 실패했습니다. 해당 소스를 건너뜁니다.");
        }
        finally
        {
            if (items is not null) Marshal.ReleaseComObject(items);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }

        return apps;
    }

    /// <summary>shell:AppsFolder 항목 Path가 패키지 AUMID(PackageFamilyName!AppId)인지 판정한다. '!'는 패키지 AUMID 구분자.</summary>
    internal static bool IsPackagedAumid(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && path.Contains('!')
        && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

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
