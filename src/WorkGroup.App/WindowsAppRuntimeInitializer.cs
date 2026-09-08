using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.WindowsAppRuntime;

namespace WorkGroup.App;

/// <summary>
/// WinAppSDK 배포 초기화를 프로세스 시작 시점(모듈 로드)에 수행한다.
///
/// 원래는 WinAppSDK targets가 같은 일을 하는 모듈 초기화자를 자동으로 심어 준다
/// (`WindowsPackageType=MSIX` + `OutputType=WinExe` 조합의 기본값). 그런데 그 코드는
/// 패키지 ID가 없는 프로세스에서 예외를 던지고, 모듈 초기화자라 그 예외가
/// <c>WorkGroup.App.dll</c>의 **타입 로드 자체**를 막는다 — 테스트 러너가 App의 ViewModel을
/// 하나도 만들 수 없게 되는 원인이 이것이었다. 그래서 csproj에서
/// <c>WindowsAppSdkDeploymentManagerInitialize=false</c>로 자동 생성을 끄고, 같은 동작을
/// 패키지 ID 확인을 앞에 두고 여기서 재현한다.
/// </summary>
internal static class WindowsAppRuntimeInitializer
{
    // GetCurrentPackageFullName이 "이 프로세스에는 패키지 ID가 없다"고 답하는 코드(appmodel.h).
    private const int AppModelErrorNoPackage = 15700;

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!ShouldInitialize(GetCurrentPackageFullNameStatus()))
            return;

        // 자동 생성 코드와 같은 옵션·같은 실패 처리다(OnErrorShowUI: 런타임이 없으면 사용자에게 안내를 띄운다).
        var result = DeploymentManager.Initialize(new DeploymentInitializeOptions { OnErrorShowUI = true });
        if (result.Status == DeploymentStatus.Ok)
            return;

        var hr = result.ExtendedError.HResult;
        Environment.Exit(hr);
        Environment.FailFast($"WindowsAppRuntime.DeploymentManager.Initialize 실패 0x{hr:X}");
    }

    /// <summary>
    /// 패키지 ID 조회 결과로 배포 초기화를 수행할지 판정한다.
    /// <see cref="AppModelErrorNoPackage"/>는 언패키지 프로세스(테스트 러너 등)를 뜻하며,
    /// 그 상태에서 <see cref="DeploymentManager"/>를 부르면 "프로세스에 패키지 ID가 없습니다"로 실패한다.
    /// 그 밖의 값은 이름이 돌아왔거나 버퍼가 모자랐다는 뜻이라 패키지 ID가 있다는 신호다.
    /// </summary>
    internal static bool ShouldInitialize(int packageFullNameStatus)
        => packageFullNameStatus != AppModelErrorNoPackage;

    // 이름 자체는 쓰지 않고 "패키지 ID가 있는가"만 본다 — 길이 0을 넘겨 상태 코드만 받는다.
    private static int GetCurrentPackageFullNameStatus()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, null);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, char[]? packageFullName);
}
