using WorkGroup.Application.Groups;
using WorkGroup.Application.Icons;
using WorkGroup.Application.Persistence;
using WorkGroup.Application.Shortcuts;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>GroupAppService 오케스트레이션 순서·실패 시 정리(M3) 검증. 인프라는 페이크로 대체.</summary>
public sealed class GroupAppServiceTests : IDisposable
{
    private readonly string _groupsDir;
    private readonly FakeIconService _icons = new();
    private readonly FakeShortcutService _shortcuts = new();
    private readonly FakeRepository _repo = new();

    public GroupAppServiceTests()
        => _groupsDir = Path.Combine(Path.GetTempPath(), "WorkGroupSvcTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_groupsDir)) Directory.Delete(_groupsDir, recursive: true); }
        catch (IOException) { }
    }

    private GroupAppService CreateSut() => new(_icons, _shortcuts, _repo, _groupsDir);
    private static AppGroup Group() => AppGroup.Create("업무").Value;

    [Fact]
    public async Task SaveAsync_성공_시_아이콘_lnk_json_순서로_저장()
    {
        var group = Group();
        var result = await CreateSut().SaveAsync(group);

        Assert.True(result.IsSuccess);
        Assert.True(_icons.Created);
        Assert.True(_shortcuts.CreatedOrUpdated);
        Assert.Single(_repo.Saved);
    }

    [Fact]
    public async Task SaveAsync_아이콘_실패_시_lnk_json_미호출()
    {
        _icons.ShouldFail = true;
        var result = await CreateSut().SaveAsync(Group());

        Assert.True(result.IsFailure);
        Assert.False(_shortcuts.CreatedOrUpdated);
        Assert.Empty(_repo.Saved);
    }

    [Fact]
    public async Task SaveAsync_lnk_실패_시_아이콘_정리_json_미호출()
    {
        _shortcuts.ShouldFail = true;
        var group = Group();

        var result = await CreateSut().SaveAsync(group);

        Assert.True(result.IsFailure);
        Assert.Empty(_repo.Saved);
        // 아이콘 파일이 정리되어야 한다.
        Assert.False(File.Exists(Path.Combine(_groupsDir, group.Id.Value, "Icons", group.Id.Value + ".ico")));
    }

    [Fact]
    public async Task SaveAsync_json_실패_시_lnk_삭제_및_아이콘_정리()
    {
        _repo.ShouldFailSave = true;
        var group = Group();

        var result = await CreateSut().SaveAsync(group);

        Assert.True(result.IsFailure);
        Assert.True(_shortcuts.Deleted);
        Assert.False(File.Exists(Path.Combine(_groupsDir, group.Id.Value, "Icons", group.Id.Value + ".ico")));
    }

    [Fact]
    public async Task DeleteAsync_lnk_아이콘_json_모두_정리()
    {
        var group = Group();
        var sut = CreateSut();
        await sut.SaveAsync(group);

        var result = await sut.DeleteAsync(group.Id);

        Assert.True(result.IsSuccess);
        Assert.True(_shortcuts.Deleted);
        Assert.False(File.Exists(Path.Combine(_groupsDir, group.Id.Value, "Icons", group.Id.Value + ".ico")));
        Assert.Empty(_repo.Saved);
    }

    [Fact]
    public async Task ClearAllAsync_모든_그룹과_폴더_제거_및_lnk_삭제_위임()
    {
        var sut = CreateSut();
        var g1 = AppGroup.Create("업무").Value;
        var g2 = AppGroup.Create("개인").Value;
        await sut.SaveAsync(g1);
        await sut.SaveAsync(g2);

        var result = await sut.ClearAllAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty((await sut.GetAllAsync()));                                 // 저장소 비워짐
        Assert.True(_shortcuts.Deleted);                                        // .lnk 삭제 위임
        Assert.False(Directory.Exists(Path.Combine(_groupsDir, g1.Id.Value)));  // 그룹 폴더 제거
        Assert.False(Directory.Exists(Path.Combine(_groupsDir, g2.Id.Value)));
    }

    [Fact]
    public async Task ClearAllAsync_그룹_없으면_성공_멱등()
    {
        var result = await CreateSut().ClearAllAsync();
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CleanupOrphansAsync_유효하지_않은_그룹폴더_제거_및_lnk_정리_위임()
    {
        var group = Group();
        var sut = CreateSut();
        await sut.SaveAsync(group); // 유효 그룹 폴더 1개 + Icons\{id}.ico 생성

        // 유효 그룹에 없는 고아 그룹 폴더 생성(폴더명은 그룹 id 형식: GUID "N")
        var orphanDir = Path.Combine(_groupsDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(orphanDir);
        File.WriteAllText(Path.Combine(orphanDir, "x.ico"), "x");

        await sut.CleanupOrphansAsync();

        Assert.False(Directory.Exists(orphanDir));                              // 고아 폴더 제거
        Assert.True(File.Exists(Path.Combine(_groupsDir, group.Id.Value, "Icons", group.Id.Value + ".ico"))); // 유효 보존
        Assert.True(_shortcuts.CleanedOrphans);                                 // .lnk 정리 위임
    }

    // ----- 페이크 -----

    private sealed class FakeIconService : IIconService
    {
        public bool ShouldFail { get; set; }
        public bool Created { get; private set; }

        public Task<Result<string>> CreateGroupIconAsync(
            GroupId groupId, IconSource source, IReadOnlyList<AppEntry> members,
            string outputDirectory, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
                return Task.FromResult(Result<string>.Fail("아이콘 실패"));

            Created = true;
            Directory.CreateDirectory(outputDirectory);
            var path = Path.Combine(outputDirectory, groupId.Value + ".ico");
            File.WriteAllText(path, "icon"); // 정리 검증용 더미
            return Task.FromResult(Result<string>.Ok(path));
        }
    }

    private sealed class FakeShortcutService : IShortcutService
    {
        public bool ShouldFail { get; set; }
        public bool CreatedOrUpdated { get; private set; }
        public bool Deleted { get; private set; }

        public Result<string> CreateOrUpdate(AppGroup group, string iconPath)
        {
            if (ShouldFail)
                return Result<string>.Fail("lnk 실패");
            CreatedOrUpdated = true;
            return Result<string>.Ok(@"C:\fake\group.lnk");
        }

        public Result Delete(AppGroup group)
        {
            Deleted = true;
            return Result.Ok();
        }

        public bool CleanedOrphans { get; private set; }

        public Result CleanupOrphans(IReadOnlyCollection<AppGroup> validGroups)
        {
            CleanedOrphans = true;
            return Result.Ok();
        }

        public string GetShortcutPath(AppGroup group) => @"C:\fake\" + group.Name + ".lnk";
    }

    private sealed class FakeRepository : IGroupRepository
    {
        public bool ShouldFailSave { get; set; }
        public List<AppGroup> Saved { get; } = new();

        public Task<IReadOnlyList<AppGroup>> LoadAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AppGroup>>(Saved.ToList());

        public Task<Result> SaveAsync(AppGroup group, CancellationToken cancellationToken = default)
        {
            if (ShouldFailSave)
                return Task.FromResult(Result.Fail("json 실패"));
            Saved.Add(group);
            return Task.FromResult(Result.Ok());
        }

        public Task<Result> DeleteAsync(GroupId id, CancellationToken cancellationToken = default)
        {
            Saved.RemoveAll(g => g.Id == id);
            return Task.FromResult(Result.Ok());
        }
    }
}
