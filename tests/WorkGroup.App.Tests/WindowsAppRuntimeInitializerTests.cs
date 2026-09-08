namespace WorkGroup.App.Tests;

/// <summary>
/// WinAppSDK 배포 초기화의 패키지 ID 가드. 네이티브 호출 자체와 실제 초기화는 헤드리스로 잴 수 없어
/// (테스트 호스트에는 패키지 ID가 원리상 없다) 판정만 갈라 낸 자리를 잰다.
/// </summary>
public class WindowsAppRuntimeInitializerTests
{
    [Fact]
    public void 패키지_ID가_없으면_초기화하지_않는다()
    {
        // APPMODEL_ERROR_NO_PACKAGE — 이 상태에서 DeploymentManager를 부르면
        // "프로세스에 패키지 ID가 없습니다"로 실패하고 모듈 초기화자가 App.dll 로드를 막는다.
        Assert.False(WindowsAppRuntimeInitializer.ShouldInitialize(15700));
    }

    [Fact]
    public void 이름이_돌아오면_초기화한다()
    {
        // ERROR_SUCCESS — 패키지 전체 이름을 받았다는 뜻이다.
        Assert.True(WindowsAppRuntimeInitializer.ShouldInitialize(0));
    }

    [Fact]
    public void 버퍼가_모자라도_초기화한다()
    {
        // ERROR_INSUFFICIENT_BUFFER — 길이 0을 넘겼을 때의 정상 응답이다. 이름이 있으니 패키지 ID도 있다.
        Assert.True(WindowsAppRuntimeInitializer.ShouldInitialize(122));
    }
}
