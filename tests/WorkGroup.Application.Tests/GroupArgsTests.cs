using WorkGroup.Infrastructure.Activation;
using Xunit;

namespace WorkGroup.Application.Tests;

/// <summary>그룹 활성화 인자 파싱/생성(D2) 검증.</summary>
public class GroupArgsTests
{
    [Theory]
    [InlineData("--group abc123", "abc123")]
    [InlineData("  --group   abc123  ", "abc123")]
    [InlineData("--group \"abc123\"", "abc123")]
    [InlineData("--GROUP abc123", "abc123")]
    public void ParseCommandLine_정상(string input, string expected)
        => Assert.Equal(expected, GroupArgs.ParseCommandLine(input));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("--group")]
    [InlineData("--other abc")]
    public void ParseCommandLine_없으면_null(string? input)
        => Assert.Null(GroupArgs.ParseCommandLine(input));

    [Fact]
    public void ParseProtocol_정상()
        => Assert.Equal("abc123", GroupArgs.ParseProtocol(new Uri("workgroup://group/abc123")));

    [Theory]
    [InlineData("http://group/abc")]
    [InlineData("workgroup://other/abc")]
    [InlineData("workgroup://group/")]
    public void ParseProtocol_잘못된_형식이면_null(string uri)
        => Assert.Null(GroupArgs.ParseProtocol(new Uri(uri)));

    [Fact]
    public void Build_왕복()
    {
        Assert.Equal("--group g1", GroupArgs.BuildCommandLineArguments("g1"));
        Assert.Equal("g1", GroupArgs.ParseCommandLine(GroupArgs.BuildCommandLineArguments("g1")));
        Assert.Equal("workgroup://group/g1", GroupArgs.BuildProtocolUri("g1"));
        Assert.Equal("g1", GroupArgs.ParseProtocol(new Uri(GroupArgs.BuildProtocolUri("g1"))));
    }
}
