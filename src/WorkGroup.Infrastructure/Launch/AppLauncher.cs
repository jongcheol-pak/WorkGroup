using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkGroup.Application.Launch;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.Infrastructure.Launch;

/// <summary>앱 실행 스펙(테스트용 순수 표현).</summary>
public readonly record struct LaunchSpec(string FileName, string Arguments, bool UseShellExecute);

/// <summary>
/// AppEntry를 실행한다. Win32는 셸 실행(.lnk/exe), Packaged는 shell:AppsFolder\{AUMID} 활성화(plan.md T11).
/// </summary>
public sealed class AppLauncher : IAppLauncher
{
    private readonly ILogger<AppLauncher> _logger;

    public AppLauncher(ILogger<AppLauncher>? logger = null)
        => _logger = logger ?? NullLogger<AppLauncher>.Instance;

    /// <summary>실행 스펙을 계산한다(순수 — 단위 테스트 대상).</summary>
    public static LaunchSpec BuildSpec(AppEntry app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Kind == AppKind.Packaged
            ? new LaunchSpec("explorer.exe", $"shell:AppsFolder\\{app.LaunchTarget}", true)
            : new LaunchSpec(app.LaunchTarget, string.Empty, true);
    }

    public Result Launch(AppEntry app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var spec = BuildSpec(app);
        try
        {
            var info = new ProcessStartInfo(spec.FileName)
            {
                Arguments = spec.Arguments,
                UseShellExecute = spec.UseShellExecute
            };
            Process.Start(info);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "앱 실행 실패: {App}", app.DisplayName);
            return Result.Fail($"'{app.DisplayName}' 실행에 실패했습니다.");
        }
    }

    public Result LaunchAsAdmin(AppEntry app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Packaged 앱(AUMID)은 runas 동사로 승격 실행할 수 없다 — 호출 전에 거부한다.
        if (app.Kind != AppKind.Win32)
            return Result.Fail("패키지 앱은 관리자 권한으로 실행할 수 없습니다.");

        try
        {
            Process.Start(BuildAdminStartInfo(app.LaunchTarget));
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "앱 관리자 권한 실행 실패: {App}", app.DisplayName);
            return Result.Fail($"'{app.DisplayName}' 실행에 실패했습니다.");
        }
    }

    /// <summary>
    /// 관리자 권한 실행용 ProcessStartInfo를 만든다(순수 — 단위 테스트 대상).
    /// MSIX 패키지 앱은 직접 runas로 비패키지 자식을 승격하면 Job Object/패키지 컨텍스트 제약으로
    /// UAC 승인 후 프로세스 생성이 실패한다. powershell.exe를 중개로 Start-Process -Verb RunAs를 호출해
    /// 패키지 컨텍스트에서 분리 승격한다(부모 종료 시 함께 종료되지도 않는다).
    /// </summary>
    public static ProcessStartInfo BuildAdminStartInfo(string launchTarget)
    {
        // PowerShell 작은따옴표 문자열 이스케이프(작은따옴표를 두 번).
        string escaped = launchTarget.Replace("'", "''");
        string psCommand = $"Start-Process -FilePath '{escaped}' -Verb RunAs";
        return new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -Command \"{psCommand}\"",
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        };
    }
}
