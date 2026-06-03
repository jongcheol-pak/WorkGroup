using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkGroup.Application.Folders;

namespace WorkGroup.Infrastructure.Folders;

/// <summary>경로를 셸 기본 동작(폴더=탐색기, 파일=기본 앱)으로 연다.</summary>
public sealed class ShellOpener : IShellOpener
{
    private readonly ILogger<ShellOpener> _logger;

    public ShellOpener(ILogger<ShellOpener>? logger = null)
        => _logger = logger ?? NullLogger<ShellOpener>.Instance;

    public void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            // UseShellExecute=true로 폴더/파일 모두 셸 기본 동작에 위임한다.
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // 없는 경로·권한·연결 프로그램 없음 등 셸 실행 실패는 흡수한다.
            _logger.LogWarning(ex, "경로 열기 실패: {Path}", path);
        }
    }
}
