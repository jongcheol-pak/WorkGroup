namespace WorkGroup.Infrastructure.Activation;

/// <summary>
/// 그룹 활성화 인자의 순수 파싱/생성 로직(plan.md D2). WinRT 의존이 없어 단위 테스트 가능.
/// 명령줄: <c>--group {id}</c>, 프로토콜: <c>workgroup://group/{id}</c>.
/// </summary>
public static class GroupArgs
{
    public const string GroupFlag = "--group";
    public const string ProtocolScheme = "workgroup";
    public const string ProtocolHost = "group";

    /// <summary>"--group {id}" 명령줄에서 id 추출. 없으면 null.</summary>
    public static string? ParseCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return null;

        var tokens = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (tokens[i].Equals(GroupFlag, StringComparison.OrdinalIgnoreCase))
            {
                var id = tokens[i + 1].Trim('"');
                return string.IsNullOrWhiteSpace(id) ? null : id;
            }
        }

        return null;
    }

    /// <summary>workgroup://group/{id} URI에서 id 추출. 없으면 null.</summary>
    public static string? ParseProtocol(Uri? uri)
    {
        if (uri is null)
            return null;
        if (!uri.Scheme.Equals(ProtocolScheme, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!uri.Host.Equals(ProtocolHost, StringComparison.OrdinalIgnoreCase))
            return null;

        var id = uri.AbsolutePath.Trim('/');
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    /// <summary>그룹 id로 .lnk 인자 문자열을 만든다.</summary>
    public static string BuildCommandLineArguments(string groupId) => $"{GroupFlag} {groupId}";

    /// <summary>그룹 id로 프로토콜 URI 문자열을 만든다.</summary>
    public static string BuildProtocolUri(string groupId) => $"{ProtocolScheme}://{ProtocolHost}/{groupId}";
}
