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

    [Fact]
    public void ParseEdit_왕복()
    {
        Assert.Equal("--edit-group g1", GroupArgs.BuildEditCommandLineArguments("g1"));
        Assert.Equal("g1", GroupArgs.ParseEditCommandLine(GroupArgs.BuildEditCommandLineArguments("g1")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("--edit-group")]
    [InlineData("--other abc")]
    public void ParseEditCommandLine_없으면_null(string? input)
        => Assert.Null(GroupArgs.ParseEditCommandLine(input));

    // --group과 --edit-group은 정확 일치라 서로 교차 매칭되지 않는다.
    [Fact]
    public void Group과_Edit_플래그는_교차_매칭되지_않는다()
    {
        Assert.Null(GroupArgs.ParseEditCommandLine("--group g1"));
        Assert.Null(GroupArgs.ParseCommandLine("--edit-group g1"));
    }

    [Theory]
    [InlineData("--silent")]
    [InlineData("  --silent  ")]
    [InlineData("--SILENT")]
    [InlineData("--group g1 --silent")]
    public void HasSilentFlag_있으면_true(string input)
        => Assert.True(GroupArgs.HasSilentFlag(input));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("--group g1")]
    [InlineData("--silentx")]
    public void HasSilentFlag_없으면_false(string? input)
        => Assert.False(GroupArgs.HasSilentFlag(input));
}
