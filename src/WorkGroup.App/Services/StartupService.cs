using Windows.ApplicationModel;

namespace WorkGroup.App.Services;

/// <summary>로그인 시 자동 시작(StartupTask) 토글(plan.md T12). 매니페스트의 TaskId와 일치해야 한다.</summary>
public sealed class StartupService
{
    private const string TaskId = "WorkGroupStartupTask";

    public async Task<bool> IsEnabledAsync()
    {
        var task = await StartupTask.GetAsync(TaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    /// <summary>자동 시작을 켜거나 끈다. 켜기는 사용자 동의가 필요할 수 있다. 최종 활성 여부를 반환.</summary>
    public async Task<bool> SetEnabledAsync(bool enable)
    {
        var task = await StartupTask.GetAsync(TaskId);

        if (!enable)
        {
            task.Disable();
            return false;
        }

        if (task.State == StartupTaskState.Disabled)
        {
            var state = await task.RequestEnableAsync();
            return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }

        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }
}
