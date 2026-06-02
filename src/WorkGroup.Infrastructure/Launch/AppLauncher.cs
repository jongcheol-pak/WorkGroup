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
}
