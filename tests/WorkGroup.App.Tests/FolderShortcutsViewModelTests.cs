using WorkGroup.App.Services;
using WorkGroup.App.Tests.Fakes;
using WorkGroup.App.ViewModels;
using WorkGroup.Domain.Common;

namespace WorkGroup.App.Tests;

/// <summary>
/// 트레이 메뉴(폴더 바로가기) 페이지 ViewModel. 두 목록 페이지가 같은 조작법을 갖는다는 것이
/// 같은 코드를 양쪽에 넣은 근거라, <see cref="WorkGroupsViewModelTests"/>와 같은 축으로 잰다.
/// 다른 점은 검색이 이름뿐 아니라 경로에도 걸린다는 것과 재정렬 인자가 int Id 목록이라는 것이다.
/// </summary>
public class FolderShortcutsViewModelTests
{
    private static (FolderShortcutsViewModel Vm, FakeFolderShortcutRepository Repo) Build(
        params (string Name, string Path)[] folders)
    {
        var repo = new FakeFolderShortcutRepository();
        foreach (var (name, path) in folders)
            repo.With(name, path);
        return (new FolderShortcutsViewModel(repo, new LocalizationService()), repo);
    }

    private static async Task<(FolderShortcutsViewModel Vm, FakeFolderShortcutRepository Repo)> LoadedAsync(
        params (string Name, string Path)[] folders)
    {
        var built = Build(folders);
        await built.Vm.LoadAsync();
        return built;
    }

    private static (string Name, string Path) Folder(string name, string? path = null)
        => (name, path ?? $@"C:\Folders\{name}");

    // --- 검색 필터 ---

    [Fact]
    public async Task 검색이_없으면_전체가_보이고_순서변경이_열려_있다()
    {
        var (vm, _) = await LoadedAsync(Folder("문서"), Folder("사진"), Folder("음악"));

        Assert.Equal(new[] { "문서", "사진", "음악" }, vm.Folders.Select(f => f.Name));
        Assert.All(vm.Folders, f => Assert.True(f.CanReorder));
    }

    [Fact]
    public async Task 이름으로_좁히면_남은_항목의_순서변경이_닫힌다()
    {
        var (vm, _) = await LoadedAsync(Folder("문서"), Folder("사진"));

        vm.SearchText = "사진";

        Assert.Equal(new[] { "사진" }, vm.Folders.Select(f => f.Name));
        Assert.False(vm.Folders[0].CanReorder);
    }

    [Fact]
    public async Task 경로로도_걸린다()
    {
        var (vm, _) = await LoadedAsync(
            ("문서", @"C:\Users\Public\Documents"),
            ("사진", @"D:\Media\Pictures"));

        vm.SearchText = "Media";

        Assert.Equal(new[] { "사진" }, vm.Folders.Select(f => f.Name));
    }

    [Fact]
    public async Task 검색어를_비우면_순서변경이_다시_열린다()
    {
        var (vm, _) = await LoadedAsync(Folder("문서"), Folder("사진"));
        vm.SearchText = "문서";

        vm.SearchText = string.Empty;

        Assert.Equal(2, vm.Folders.Count);
        Assert.All(vm.Folders, f => Assert.True(f.CanReorder));
    }

    [Fact]
    public async Task 공백만_입력한_것은_검색이_아니다()
    {
        var (vm, _) = await LoadedAsync(Folder("문서"), Folder("사진"));

        vm.SearchText = "   ";

        Assert.Equal(2, vm.Folders.Count);
        Assert.All(vm.Folders, f => Assert.True(f.CanReorder));
    }

    // --- 순서 변경 ---

    [Fact]
    public async Task 검색_중에는_순서를_바꾸지_않고_저장도_부르지_않는다()
    {
        var (vm, repo) = await LoadedAsync(Folder("문서"), Folder("사진"), Folder("음악"));
        vm.SearchText = "문서";

        await vm.MoveAsync(0, 2);

        Assert.Equal(0, repo.ReorderCallCount);
        Assert.Null(repo.LastReorder);
    }

    [Fact]
    public async Task 제자리로_옮기면_아무것도_하지_않는다()
    {
        var (vm, repo) = await LoadedAsync(Folder("문서"), Folder("사진"));

        await vm.MoveAsync(1, 1);

        Assert.Equal(0, repo.ReorderCallCount);
        Assert.Equal(new[] { "문서", "사진" }, vm.Folders.Select(f => f.Name));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public async Task 범위_밖_인덱스는_무동작이다(int from, int to)
    {
        var (vm, repo) = await LoadedAsync(Folder("문서"), Folder("사진"));

        await vm.MoveAsync(from, to);

        Assert.Equal(0, repo.ReorderCallCount);
        Assert.Equal(new[] { "문서", "사진" }, vm.Folders.Select(f => f.Name));
    }

    [Fact]
    public async Task 이동하면_목록과_저장_순서가_함께_바뀐다()
    {
        var (vm, repo) = await LoadedAsync(Folder("문서"), Folder("사진"), Folder("음악"));

        await vm.MoveAsync(0, 2);

        Assert.Equal(new[] { "사진", "음악", "문서" }, vm.Folders.Select(f => f.Name));
        Assert.Equal(new[] { 2, 3, 1 }, repo.LastReorder);
    }

    // --- 상태 메시지 ---

    [Fact]
    public async Task 저장에_실패하면_그_사유가_안내로_남는다()
    {
        var (vm, repo) = await LoadedAsync(Folder("문서"), Folder("사진"));
        repo.ReorderResult = Result.Fail("폴더 순서를 저장하지 못했습니다.");

        await vm.MoveAsync(0, 1);

        Assert.Equal("폴더 순서를 저장하지 못했습니다.", vm.StatusMessage);
        Assert.True(vm.HasStatus);
    }

    [Fact]
    public async Task 저장에_실패해도_목록은_되돌리지_않는다()
    {
        var (vm, repo) = await LoadedAsync(Folder("문서"), Folder("사진"));
        repo.ReorderResult = Result.Fail("폴더 순서를 저장하지 못했습니다.");

        await vm.MoveAsync(0, 1);

        Assert.Equal(new[] { "사진", "문서" }, vm.Folders.Select(f => f.Name));
    }

    [Fact]
    public async Task 저장에_성공하면_안내가_남지_않는다()
    {
        var (vm, _) = await LoadedAsync(Folder("문서"), Folder("사진"));

        await vm.MoveAsync(0, 1);

        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.False(vm.HasStatus);
    }

    [Fact]
    public async Task 다시_불러오면_이전_실패_안내가_사라진다()
    {
        var (vm, repo) = await LoadedAsync(Folder("문서"), Folder("사진"));
        repo.ReorderResult = Result.Fail("폴더 순서를 저장하지 못했습니다.");
        await vm.MoveAsync(0, 1);

        await vm.LoadAsync();

        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.False(vm.HasStatus);
    }

    // --- 빈 상태 ---

    [Fact]
    public async Task 등록된_폴더가_없을_때만_빈_상태다()
    {
        var (empty, _) = await LoadedAsync();
        Assert.True(empty.IsEmpty);

        var (vm, _) = await LoadedAsync(Folder("문서"));
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task 검색_결과가_0건이어도_빈_상태는_아니다()
    {
        var (vm, _) = await LoadedAsync(Folder("문서"));

        vm.SearchText = "없는이름";

        Assert.Empty(vm.Folders);
        Assert.False(vm.IsEmpty);
    }
}
