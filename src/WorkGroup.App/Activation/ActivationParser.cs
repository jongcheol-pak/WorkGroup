using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using WorkGroup.Infrastructure.Activation;

namespace WorkGroup.App.Activation;

/// <summary>
/// 앱 활성화 인자(WinRT)에서 그룹 식별자를 추출한다(plan.md D2).
/// 순수 파싱은 <see cref="GroupArgs"/>에 위임하고 여기서는 활성화 종류만 디스패치한다.
/// </summary>
public static class ActivationParser
{
    /// <summary>활성화 인자에서 그룹 id를 찾는다. 없으면 null(일반 실행 → 메인 창).</summary>
    public static string? TryGetGroupId(AppActivationArguments args)
    {
        return args.Kind switch
        {
            ExtendedActivationKind.Launch when args.Data is ILaunchActivatedEventArgs launch
                => GroupArgs.ParseCommandLine(launch.Arguments),
            ExtendedActivationKind.Protocol when args.Data is IProtocolActivatedEventArgs protocol
                => GroupArgs.ParseProtocol(protocol.Uri),
            _ => null
        };
    }

    /// <summary>활성화 인자에서 "그룹 수정" 대상 id를 찾는다(명령줄 별칭만). 없으면 null.</summary>
    public static string? TryGetEditGroupId(AppActivationArguments args)
    {
        return args.Kind switch
        {
            ExtendedActivationKind.Launch when args.Data is ILaunchActivatedEventArgs launch
                => GroupArgs.ParseEditCommandLine(launch.Arguments),
            _ => null
        };
    }
}
