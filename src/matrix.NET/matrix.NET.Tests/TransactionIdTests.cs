namespace TeamBanana.MatrixDotNet.Tests;

public class TransactionIdTests
{
    [Fact]
    public void New_ReturnsAVersion4UuidAsHexDigits()
    {
        var id = TransactionId.New();

        Assert.Matches("^[0-9a-f]{12}4[0-9a-f]{19}$", id);
    }

    [Fact]
    public void New_ReturnsADifferentIdEachTime()
    {
        Assert.NotEqual(TransactionId.New(), TransactionId.New());
    }
}
