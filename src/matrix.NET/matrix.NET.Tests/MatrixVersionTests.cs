namespace TeamBanana.MatrixDotNet.Tests;

public class MatrixVersionTests
{
    [Fact]
    public void ComparesByMajorThenMinorNumerically()
    {
        Assert.True(new MatrixVersion(1, 15) > new MatrixVersion(1, 3));
        Assert.True(new MatrixVersion(2, 0) > new MatrixVersion(1, 19));
        Assert.True(new MatrixVersion(1, 1) <= MatrixVersion.Minimum);
        Assert.Equal(new MatrixVersion(1, 3), new MatrixVersion(1, 3));
    }

    [Fact]
    public void FormatsAsTheSpecWritesIt()
    {
        Assert.Equal("v1.15", new MatrixVersion(1, 15).ToString());
    }

    [Fact]
    public void RejectsNegativeNumbers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatrixVersion(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatrixVersion(1, -1));
    }
}
