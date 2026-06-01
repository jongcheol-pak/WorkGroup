using WorkGroup.Domain.Groups;
using WorkGroup.Infrastructure.Persistence;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>JsonGroupRepository 영속화/복구 동작 검증. 임시 폴더를 주입해 격리한다.</summary>
public sealed class JsonGroupRepositoryTests : IDisposable
{
    private readonly string _dir;

    public JsonGroupRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "WorkGroupTests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { /* 테스트 정리 실패 무시 */ }
    }

    private JsonGroupRepository CreateSut() => new(_dir);

    private static AppGroup SampleGroup(string name)
    {
        var group = AppGroup.Create(name).Value;
        group.AddApp(new AppEntry("Word", @"C:\Office\winword.exe", AppKind.Win32, @"C:\Office\winword.exe"));
        group.AddApp(new AppEntry("계산기", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", AppKind.Packaged));
        return group;
    }

    [Fact]
    public async Task LoadAll_파일_없으면_빈_목록()
    {
        var sut = CreateSut();
        var groups = await sut.LoadAllAsync();
        Assert.Empty(groups);
    }

    [Fact]
    public async Task Save_후_Load_라운드트립()
    {
        var sut = CreateSut();
        var group = SampleGroup("업무");

        var saveResult = await sut.SaveAsync(group);
        Assert.True(saveResult.IsSuccess);

        // 새 인스턴스로 로드(재시작 시나리오)
        var loaded = await CreateSut().LoadAllAsync();

        var one = Assert.Single(loaded);
        Assert.Equal(group.Id, one.Id);
        Assert.Equal("업무", one.Name);
        Assert.Equal(2, one.Apps.Count);
        Assert.Contains(one.Apps, a => a.Kind == AppKind.Packaged);
    }

    [Fact]
    public async Task Save_같은_Id_재저장은_갱신()
    {
        var sut = CreateSut();
        var group = SampleGroup("업무");
        await sut.SaveAsync(group);

        group.Rename("업무 변경");
        await sut.SaveAsync(group);

        var loaded = await sut.LoadAllAsync();
        var one = Assert.Single(loaded);
        Assert.Equal("업무 변경", one.Name);
    }

    [Fact]
    public async Task Save_다른_그룹은_누적()
    {
        var sut = CreateSut();
        await sut.SaveAsync(SampleGroup("그룹A"));
        await sut.SaveAsync(SampleGroup("그룹B"));

        var loaded = await sut.LoadAllAsync();
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public async Task Delete_그룹_제거()
    {
        var sut = CreateSut();
        var group = SampleGroup("업무");
        await sut.SaveAsync(group);

        var result = await sut.DeleteAsync(group.Id);
        Assert.True(result.IsSuccess);
        Assert.Empty(await sut.LoadAllAsync());
    }

    [Fact]
    public async Task Delete_없는_그룹은_멱등_성공()
    {
        var sut = CreateSut();
        var result = await sut.DeleteAsync(GroupId.New());
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Load_손상_파일이면_백업하고_빈_목록()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "groups.json"), "{ 망가진 JSON ]");

        var sut = CreateSut();
        var groups = await sut.LoadAllAsync();

        Assert.Empty(groups);
        Assert.True(File.Exists(Path.Combine(_dir, "groups.json.corrupt.bak")));
    }

    [Fact]
    public async Task Save_파일에_schemaVersion_포함()
    {
        var sut = CreateSut();
        await sut.SaveAsync(SampleGroup("업무"));

        var json = await File.ReadAllTextAsync(Path.Combine(_dir, "groups.json"));
        Assert.Contains("\"SchemaVersion\"", json);
    }
}
