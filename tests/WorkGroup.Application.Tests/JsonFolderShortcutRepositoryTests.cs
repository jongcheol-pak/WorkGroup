using WorkGroup.Infrastructure.Persistence;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>JsonFolderShortcutRepository 영속화/복구 동작 검증. 임시 파일을 주입해 격리한다.</summary>
public sealed class JsonFolderShortcutRepositoryTests : IDisposable
{
    private readonly string _dir;
    private readonly string _filePath;

    public JsonFolderShortcutRepositoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "WorkGroupFolderTests", Guid.NewGuid().ToString("N"));
        _filePath = Path.Combine(_dir, "folders.json");
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

    private JsonFolderShortcutRepository CreateSut() => new(_filePath);

    [Fact]
    public async Task LoadAll_파일_없으면_빈_목록()
    {
        var sut = CreateSut();
        Assert.Empty(await sut.LoadAllAsync());
    }

    [Fact]
    public async Task Add_후_Load_라운드트립_Id는_1부터()
    {
        var sut = CreateSut();
        var added = await sut.AddAsync("Module", @"D:\Module");

        Assert.True(added.IsSuccess);
        Assert.Equal(1, added.Value.Id);

        var loaded = await sut.LoadAllAsync();
        var single = Assert.Single(loaded);
        Assert.Equal("Module", single.Name);
        Assert.Equal(@"D:\Module", single.Path);
        Assert.Equal(1, single.Id);
    }

    [Fact]
    public async Task ClearAll_모든_폴더_제거()
    {
        var sut = CreateSut();
        await sut.AddAsync("Module", @"D:\Module");
        await sut.AddAsync("Source", @"D:\Source");

        var result = await sut.ClearAllAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(await sut.LoadAllAsync());
    }

    [Fact]
    public async Task ClearAll_비어있어도_성공_멱등()
    {
        var sut = CreateSut();
        Assert.True((await sut.ClearAllAsync()).IsSuccess);
    }

    [Fact]
    public async Task Add_Id는_단조_증가()
    {
        var sut = CreateSut();
        await sut.AddAsync("Module", @"D:\Module");
        var second = await sut.AddAsync("Source", @"D:\Source");

        Assert.True(second.IsSuccess);
        Assert.Equal(2, second.Value.Id);
    }

    [Fact]
    public async Task Add_경로_중복이면_실패()
    {
        var sut = CreateSut();
        await sut.AddAsync("Module", @"D:\Module");
        var dup = await sut.AddAsync("다른이름", @"d:\module"); // 대소문자 무시

        Assert.True(dup.IsFailure);
        // 로컬라이저 미주입(NullLocalizer)이면 리소스 키를 그대로 반환한다 — 올바른 메시지 키 사용을 검증(plan.md T8/D5).
        Assert.Equal("Infra_Folder_Duplicate", dup.Error);
    }

    [Fact]
    public async Task Update_이름과_경로_갱신()
    {
        var sut = CreateSut();
        var added = await sut.AddAsync("Module", @"D:\Module");
        var result = await sut.UpdateAsync(added.Value.Id, "모듈", @"D:\Module2");

        Assert.True(result.IsSuccess);
        var loaded = await sut.LoadAllAsync();
        var single = Assert.Single(loaded);
        Assert.Equal("모듈", single.Name);
        Assert.Equal(@"D:\Module2", single.Path);
    }

    [Fact]
    public async Task Update_없는_Id면_실패()
    {
        var sut = CreateSut();
        var result = await sut.UpdateAsync(99, "x", @"D:\x");

        Assert.True(result.IsFailure);
        Assert.Equal("Infra_Folder_NotFound", result.Error);
    }

    [Fact]
    public async Task Delete_후_제외_없는_Id는_멱등()
    {
        var sut = CreateSut();
        var added = await sut.AddAsync("Module", @"D:\Module");

        Assert.True((await sut.DeleteAsync(added.Value.Id)).IsSuccess);
        Assert.Empty(await sut.LoadAllAsync());

        // 없는 Id 삭제는 성공으로 간주(멱등).
        Assert.True((await sut.DeleteAsync(added.Value.Id)).IsSuccess);
    }

    [Fact]
    public async Task 손상_파일_로드시_빈_목록과_백업생성()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(_filePath, "{ 손상된 JSON");

        var sut = CreateSut();
        Assert.Empty(await sut.LoadAllAsync());
        Assert.True(File.Exists(_filePath + ".corrupt.bak"));
    }

    [Fact]
    public async Task Reorder_후_로드하면_지정한_순서로_나온다()
    {
        var sut = CreateSut();
        await sut.AddAsync("A", @"D:\A");
        await sut.AddAsync("B", @"D:\B");
        await sut.AddAsync("C", @"D:\C");

        var result = await sut.ReorderAsync([3, 1, 2]);
        Assert.True(result.IsSuccess);

        // 새 인스턴스로 로드해 직렬화까지 순서가 보존되는지 본다(재시작 시나리오).
        var loaded = await CreateSut().LoadAllAsync();
        Assert.Equal(["C", "A", "B"], loaded.Select(f => f.Name));
    }

    [Fact]
    public async Task Reorder_목록에_빠진_폴더는_뒤에_남는다()
    {
        var sut = CreateSut();
        await sut.AddAsync("A", @"D:\A");
        await sut.AddAsync("B", @"D:\B");
        await sut.AddAsync("C", @"D:\C");

        // Id 2(B)를 빼고 재정렬 — B가 유실되지 않고 뒤에 붙어야 한다.
        await sut.ReorderAsync([3, 1]);

        var loaded = await CreateSut().LoadAllAsync();
        Assert.Equal(["C", "A", "B"], loaded.Select(f => f.Name));
    }

    [Fact]
    public async Task Reorder_저장에_없는_id는_무시된다()
    {
        var sut = CreateSut();
        await sut.AddAsync("A", @"D:\A");
        await sut.AddAsync("B", @"D:\B");

        // 이미 삭제된 폴더의 Id가 섞여 들어와도 예외 없이 나머지 순서만 반영한다.
        var result = await sut.ReorderAsync([99, 2, 1]);
        Assert.True(result.IsSuccess);

        var loaded = await CreateSut().LoadAllAsync();
        Assert.Equal(["B", "A"], loaded.Select(f => f.Name));
    }
}
