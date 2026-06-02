using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Management.Deployment;
using Windows.Storage.Streams;

namespace WorkGroup.Infrastructure.Icons;

/// <summary>
/// 패키지(Store/UWP) 앱의 공식 로고 스트림을 AUMID로 추출한다(plan.md T1).
/// package.Logo 경로가 없는 앱(Teams/Discord 등)도 셸이 제공하는 로고를 직접 얻어 아이콘 누락을 막는다.
/// WinUI 타입(ImageSource)에 의존하지 않도록 WinRT 스트림까지만 책임지고, 소비자가 자기 타입으로 변환한다.
/// </summary>
public static class PackagedAppIcon
{
    /// <summary>
    /// AUMID로 패키지 앱을 찾아 요청 크기에 맞는 로고 스트림을 연다. 실패하면 예외 없이 null(호출자 폴백).
    /// </summary>
    /// <param name="aumid">패키지 앱의 AUMID(형식: PackageFamilyName!AppId).</param>
    /// <param name="size">요청 로고 크기(셸이 스케일/대비 자산을 자동 선택).</param>
    public static async Task<IRandomAccessStream?> OpenLogoStreamAsync(
        string aumid, uint size, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(aumid))
            return null;

        try
        {
            // 패키지 열거·로고 참조 획득은 동기 WinRT 호출이라 UI 스레드를 막지 않도록 오프로드한다.
            // 스트림 열기(OpenReadAsync)는 비동기이므로 Task.Run 밖에서 await한다.
            var logoRef = await Task.Run(() => ResolveLogoReference(aumid, size), cancellationToken)
                .ConfigureAwait(false);
            if (logoRef is null)
                return null;

            return await logoRef.OpenReadAsync().AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 패키지 미발견·권한·구버전 등 모든 실패는 null로 흡수하고 호출자 폴백에 맡긴다.
            return null;
        }
    }

    /// <summary>AUMID에서 PackageFamilyName을 파싱해 패키지를 찾고 로고 참조를 반환한다(동기 WinRT).</summary>
    private static RandomAccessStreamReference? ResolveLogoReference(string aumid, uint size)
    {
        // GetAppListEntries()는 Windows 10.0.19041.0+ 에서만 지원된다(InstalledAppInventory.TryMapPackage와 동일 가드).
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return null;

        // AUMID = "PackageFamilyName!AppId". '!' 앞부분이 FamilyName(없으면 전체를 FamilyName으로 본다).
        var bang = aumid.IndexOf('!');
        var familyName = bang >= 0 ? aumid[..bang] : aumid;
        if (string.IsNullOrWhiteSpace(familyName))
            return null;

        var manager = new PackageManager();
        Package? package = manager.FindPackagesForUser(string.Empty)
            .FirstOrDefault(p => string.Equals(p.Id.FamilyName, familyName, StringComparison.OrdinalIgnoreCase));
        if (package is null)
            return null;

        var entries = package.GetAppListEntries();
        if (entries.Count == 0)
            return null;

        // AUMID가 정확히 일치하는 앱 엔트리를 우선. 없으면 첫 엔트리(주 진입점)로 폴백한다.
        var entry = entries.FirstOrDefault(e => string.Equals(e.AppUserModelId, aumid, StringComparison.Ordinal))
            ?? entries[0];

        return (RandomAccessStreamReference)entry.DisplayInfo.GetLogo(new Size(size, size));
    }
}
