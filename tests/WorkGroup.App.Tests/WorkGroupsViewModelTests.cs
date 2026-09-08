using WorkGroup.App.Services;
using WorkGroup.App.Tests.Fakes;
using WorkGroup.App.ViewModels;
using WorkGroup.Domain.Common;
using WorkGroup.Domain.Groups;

namespace WorkGroup.App.Tests;

/// <summary>
/// 작업 그룹 페이지 ViewModel. 검색 필터가 순서 변경 핸들을 끄는지, 검색 중 순서 변경이 무동작인지,
/// 저장 실패가 안내로 이어지는지를 잰다 — 조건식이 아니라 밖에서 보이는 결과(목록·플래그·저장 인자)로만 단언한다.
/// </summary>
public class WorkGroupsViewModelTests
{
    private static AppGroup Group(string name, params string[] appNames)
        => AppGroup.Restore(
            GroupId.New(),
            name,
            IconSource.DefaultBuiltIn,
            appNames.Select(a => new AppEntry(a, $@"C:\Apps\{a}.exe", AppKind.Win32)));

    private static (WorkGroupsViewModel Vm, FakeGroupAppService Service) Build(params AppGroup[] groups)
    {
        var service = new FakeGroupAppService { Groups = groups };
        return (new WorkGroupsViewModel(service, new LocalizationService()), service);
    }

    private static async Task<(WorkGroupsViewModel Vm, FakeGroupAppService Service)> LoadedAsync(params AppGroup[] groups)
    {
        var built = Build(groups);
        await built.Vm.LoadAsync();
        return built;
    }

    // --- 검색 필터 ---

    [Fact]
    public async Task 검색이_없으면_전체가_보이고_순서변경이_열려_있다()
    {
        var (vm, _) = await LoadedAsync(Group("가"), Group("나"), Group("다"));

        Assert.Equal(new[] { "가", "나", "다" }, vm.Groups.Select(g => g.Name));
        Assert.All(vm.Groups, g => Assert.True(g.CanReorder));
    }

    [Fact]
    public async Task 이름으로_좁히면_남은_항목의_순서변경이_닫힌다()
    {
        var (vm, _) = await LoadedAsync(Group("문서"), Group("사진"), Group("음악"));

        vm.SearchText = "사진";

        Assert.Equal(new[] { "사진" }, vm.Groups.Select(g => g.Name));
        Assert.False(vm.Groups[0].CanReorder);
    }

    [Fact]
    public async Task 멤버_앱_이름으로도_걸린다()
    {
        var (vm, _) = await LoadedAsync(Group("문서", "한글"), Group("사진", "포토샵"));

        vm.SearchText = "포토샵";

        Assert.Equal(new[] { "사진" }, vm.Groups.Select(g => g.Name));
    }

    [Fact]
    public async Task 검색어를_비우면_순서변경이_다시_열린다()
    {
        var (vm, _) = await LoadedAsync(Group("문서"), Group("사진"));
        vm.SearchText = "문서";

        vm.SearchText = string.Empty;

        Assert.Equal(2, vm.Groups.Count);
        Assert.All(vm.Groups, g => Assert.True(g.CanReorder));
    }

    [Fact]
    public async Task 공백만_입력한_것은_검색이_아니다()
    {
        var (vm, _) = await LoadedAsync(Group("문서"), Group("사진"));

        vm.SearchText = "   ";

        Assert.Equal(2, vm.Groups.Count);
        Assert.All(vm.Groups, g => Assert.True(g.CanReorder));
    }

    [Fact]
    public async Task 검색은_대소문자를_무시한다()
    {
        var (vm, _) = await LoadedAsync(Group("Photos"), Group("Docs"));

        vm.SearchText = "photos";

        Assert.Equal(new[] { "Photos" }, vm.Groups.Select(g => g.Name));
    }

    // --- 순서 변경 ---

    [Fact]
    public async Task 검색_중에는_순서를_바꾸지_않고_저장도_부르지_않는다()
    {
        var (vm, service) = await LoadedAsync(Group("가"), Group("나"), Group("다"));
        vm.SearchText = "가";

        await vm.MoveAsync(0, 2);

        Assert.Equal(0, service.ReorderCallCount);
        Assert.Null(service.LastReorder);
    }

    [Fact]
    public async Task 제자리로_옮기면_아무것도_하지_않는다()
    {
        var (vm, service) = await LoadedAsync(Group("가"), Group("나"));

        await vm.MoveAsync(1, 1);

        Assert.Equal(0, service.ReorderCallCount);
        Assert.Equal(new[] { "가", "나" }, vm.Groups.Select(g => g.Name));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public async Task 범위_밖_인덱스는_무동작이다(int from, int to)
    {
        var (vm, service) = await LoadedAsync(Group("가"), Group("나"));

        await vm.MoveAsync(from, to);

        Assert.Equal(0, service.ReorderCallCount);
        Assert.Equal(new[] { "가", "나" }, vm.Groups.Select(g => g.Name));
    }

    [Fact]
    public async Task 이동하면_목록과_저장_순서가_함께_바뀐다()
    {
        var (vm, service) = await LoadedAsync(Group("가"), Group("나"), Group("다"));
        var expected = new[] { vm.Groups[1].Group.Id.Value, vm.Groups[2].Group.Id.Value, vm.Groups[0].Group.Id.Value };

        await vm.MoveAsync(0, 2);

        Assert.Equal(new[] { "나", "다", "가" }, vm.Groups.Select(g => g.Name));
        Assert.Equal(expected, service.LastReorder!.Select(id => id.Value));
    }

    // --- 상태 메시지 ---

    [Fact]
    public async Task 저장에_실패하면_그_사유가_안내로_남는다()
    {
        var (vm, service) = await LoadedAsync(Group("가"), Group("나"));
        service.ReorderResult = Result.Fail("그룹 순서를 저장하지 못했습니다.");

        await vm.MoveAsync(0, 1);

        Assert.Equal("그룹 순서를 저장하지 못했습니다.", vm.StatusMessage);
        Assert.True(vm.HasStatus);
    }

    [Fact]
    public async Task 저장에_실패해도_목록은_되돌리지_않는다()
    {
        var (vm, service) = await LoadedAsync(Group("가"), Group("나"));
        service.ReorderResult = Result.Fail("그룹 순서를 저장하지 못했습니다.");

        await vm.MoveAsync(0, 1);

        Assert.Equal(new[] { "나", "가" }, vm.Groups.Select(g => g.Name));
    }

    [Fact]
    public async Task 저장에_성공하면_안내가_남지_않는다()
    {
        var (vm, _) = await LoadedAsync(Group("가"), Group("나"));

        await vm.MoveAsync(0, 1);

        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.False(vm.HasStatus);
    }

    [Fact]
    public async Task 다시_불러오면_이전_실패_안내가_사라진다()
    {
        var (vm, service) = await LoadedAsync(Group("가"), Group("나"));
        service.ReorderResult = Result.Fail("그룹 순서를 저장하지 못했습니다.");
        await vm.MoveAsync(0, 1);

        await vm.LoadAsync();

        Assert.Equal(string.Empty, vm.StatusMessage);
        Assert.False(vm.HasStatus);
    }

    // --- 빈 상태 ---

    [Fact]
    public async Task 등록된_그룹이_없을_때만_빈_상태다()
    {
        var (empty, _) = await LoadedAsync();
        Assert.True(empty.IsEmpty);

        var (vm, _) = await LoadedAsync(Group("가"));
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task 검색_결과가_0건이어도_빈_상태는_아니다()
    {
        var (vm, _) = await LoadedAsync(Group("가"));

        vm.SearchText = "없는이름";

        Assert.Empty(vm.Groups);
        Assert.False(vm.IsEmpty);
    }
}
