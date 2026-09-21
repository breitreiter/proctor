using FetchCli;

public class OptionsTests
{
    [Fact]
    public void Url_IsThePositionalArgument()
    {
        var o = Options.Parse(["http://example.test/"]);
        Assert.Equal("http://example.test/", o.Url);
        Assert.Equal(30, o.TimeoutSeconds);
    }

    [Fact]
    public void Timeout_IsParsed() =>
        Assert.Equal(5, Options.Parse(["--timeout", "5", "http://example.test/"]).TimeoutSeconds);

    [Fact]
    public void UnknownFlag_Throws() =>
        Assert.Throws<ArgumentException>(() => Options.Parse(["--bogus", "http://example.test/"]));

    [Fact]
    public void MissingUrl_Throws() =>
        Assert.Throws<ArgumentException>(() => Options.Parse([]));
}
