namespace TeamBanana.MatrixDotNet.Tests;

public class MatrixServerTests
{
    private static readonly Uri Homeserver = new("https://matrix.example.org/");

    [Fact]
    public void Constructor_RejectsNullHomeserver()
    {
        Assert.Throws<ArgumentNullException>(() => new MatrixServer(null!));
    }

    [Fact]
    public void Options_DefaultWhenOmitted()
    {
        var server = new MatrixServer(Homeserver);

        Assert.True(server.Options.AutomaticDecompression);
    }

    [Fact]
    public void Options_KeepsTheGivenInstance()
    {
        var options = new ServerOptions { AutomaticDecompression = false };

        Assert.Same(options, new MatrixServer(Homeserver, options).Options);
        Assert.Same(options, new MatrixServer(Homeserver, new HttpClient(), options).Options);
        Assert.Same(options, new MatrixServer(Homeserver, () => new HttpClient(), options).Options);
    }
}
