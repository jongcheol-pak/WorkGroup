using WorkGroup.Domain.Common;

namespace WorkGroup.Domain.Groups;

/// <summary>
/// 작업 그룹 — 여러 앱을 묶은 단위. 작업 표시줄 핀의 대상이 된다.
/// 불변식: 이름은 비어 있을 수 없고, 같은 실행 대상의 앱은 중복 추가되지 않는다. 멤버 0개는 허용한다.
/// </summary>
public sealed class AppGroup
{
    private readonly List<AppEntry> _apps = new();

    private AppGroup(GroupId id, string name, IconSource icon, bool showPopupHeader)
    {
        Id = id;
        Name = name;
        Icon = icon;
        ShowPopupHeader = showPopupHeader;
    }

    public GroupId Id { get; }
    public string Name { get; private set; }
    public IconSource Icon { get; private set; }
    public IReadOnlyList<AppEntry> Apps => _apps;

    /// <summary>핀 팝업에 그룹 이름 헤더를 표시할지 여부(그룹별 설정).</summary>
    public bool ShowPopupHeader { get; private set; }

    /// <summary>새 그룹을 생성한다. 아이콘 미지정 시 기본 내장 아이콘을 사용한다.</summary>
    public static Result<AppGroup> Create(string name, IconSource? icon = null, bool showPopupHeader = true)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<AppGroup>.Fail("그룹 이름은 필수입니다.");

        return Result<AppGroup>.Ok(
            new AppGroup(GroupId.New(), name.Trim(), icon ?? IconSource.DefaultBuiltIn, showPopupHeader));
    }

    /// <summary>영속화된 데이터로부터 그룹을 복원한다(리포지토리 전용).</summary>
    public static AppGroup Restore(
        GroupId id, string name, IconSource icon, IEnumerable<AppEntry> apps, bool showPopupHeader = true)
    {
        var group = new AppGroup(id, name, icon, showPopupHeader);
        foreach (var app in apps)
        {
            if (!group._apps.Any(a => a.SameTarget(app.LaunchTarget)))
                group._apps.Add(app);
        }
        return group;
    }

    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Fail("그룹 이름은 필수입니다.");

        Name = name.Trim();
        return Result.Ok();
    }

    /// <summary>앱을 추가한다. 같은 실행 대상이 이미 있으면 실패를 반환한다.</summary>
    public Result AddApp(AppEntry app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (_apps.Any(a => a.SameTarget(app.LaunchTarget)))
            return Result.Fail($"이미 추가된 앱입니다: {app.DisplayName}");

        _apps.Add(app);
        return Result.Ok();
    }

    /// <summary>실행 대상으로 앱을 제거한다. 해당 앱이 없으면 실패를 반환한다.</summary>
    public Result RemoveApp(string launchTarget)
    {
        var target = _apps.FirstOrDefault(a => a.SameTarget(launchTarget));
        if (target is null)
            return Result.Fail("그룹에 없는 앱입니다.");

        _apps.Remove(target);
        return Result.Ok();
    }

    public void SetIcon(IconSource icon)
    {
        ArgumentNullException.ThrowIfNull(icon);
        Icon = icon;
    }

    /// <summary>핀 팝업의 그룹 이름 헤더 표시 여부를 설정한다(편집 시).</summary>
    public void SetShowPopupHeader(bool value) => ShowPopupHeader = value;
}
