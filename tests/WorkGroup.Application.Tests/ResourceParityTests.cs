using System.Xml.Linq;

namespace WorkGroup.Application.Tests;

/// <summary>
/// 다국어 리소스(.resw) 무결성 검증(plan.md T10/D11). 마크업 익스텐션이 런타임 평가라
/// 누락 키/미번역을 빌드만으로는 못 잡으므로, 4개 언어 resw의 키 집합 동일성과 빈 값 부재를
/// 순수 XML 파싱으로 검사한다. resw 위치는 실행 디렉터리에서 상위로 올라가며 탐색한다.
/// </summary>
public class ResourceParityTests
{
    private static readonly string[] Languages = ["ko-KR", "en-US", "ja-JP", "zh-Hans"];

    [Fact]
    public void 모든_언어_resw의_키집합이_동일하다()
    {
        var byLang = Languages.ToDictionary(l => l, l => LoadEntries(l).Select(e => e.Key).ToHashSet());
        var reference = byLang["ko-KR"];

        foreach (var lang in Languages)
        {
            var missing = reference.Except(byLang[lang]).ToList();
            var extra = byLang[lang].Except(reference).ToList();
            Assert.True(missing.Count == 0, $"{lang}에 누락된 키: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0, $"{lang}에 잉여 키: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void 모든_resw_값이_비어있지_않다()
    {
        foreach (var lang in Languages)
            foreach (var (key, value) in LoadEntries(lang))
                Assert.False(string.IsNullOrWhiteSpace(value), $"{lang}/{key} 값이 비어 있습니다.");
    }

    // 한 언어 resw의 (키, 값) 목록을 파싱한다(<data name><value> 구조, 주석/스키마/resheader 제외).
    private static IReadOnlyList<(string Key, string Value)> LoadEntries(string lang)
    {
        var path = Path.Combine(StringsDirectory(), lang, "Resources.resw");
        var doc = XDocument.Load(path);
        return doc.Root!.Elements("data")
            .Where(d => d.Attribute("name") is not null)
            .Select(d => (d.Attribute("name")!.Value, d.Element("value")?.Value ?? string.Empty))
            .ToList();
    }

    // 실행 디렉터리에서 상위로 올라가며 src/WorkGroup.App/Strings 를 찾는다.
    private static string StringsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "WorkGroup.App", "Strings");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("src/WorkGroup.App/Strings 를 찾을 수 없습니다(리포지토리 루트 탐색 실패).");
    }
}
