using System.Net;
using TeamBanana.MatrixDotNet.Compatibility;

namespace TeamBanana.MatrixDotNet.Tests;

public class HomeserverQuirksTests
{
    [Theory]
    // Synapse's answers for a used or expired refresh token
    [InlineData(HttpStatusCode.Forbidden, MatrixErrorCodes.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest, MatrixErrorCodes.BadJson)]
    [InlineData(HttpStatusCode.NotFound, MatrixErrorCodes.Unrecognized)]
    public void IsFinalRefreshFailure_ClientErrors(HttpStatusCode status, string errorCode)
    {
        Assert.True(HomeserverQuirks.IsFinalRefreshFailure(new MatrixException(status, errorCode, null)));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, MatrixErrorCodes.LimitExceeded)]
    // Rate limiting is temporary whichever way the homeserver reports it
    [InlineData(HttpStatusCode.BadRequest, MatrixErrorCodes.LimitExceeded)]
    [InlineData(HttpStatusCode.InternalServerError, MatrixErrorCodes.Unknown)]
    [InlineData(HttpStatusCode.BadGateway, MatrixErrorCodes.Unknown)]
    public void IsFinalRefreshFailure_TemporaryErrors(HttpStatusCode status, string errorCode)
    {
        Assert.False(HomeserverQuirks.IsFinalRefreshFailure(new MatrixException(status, errorCode, null)));
    }

    [Fact]
    public void IsFinalRefreshFailure_LeavesLockedAccountsToTheirOwnState()
    {
        Assert.False(HomeserverQuirks.IsFinalRefreshFailure(
            new MatrixUserLockedException(HttpStatusCode.Unauthorized, null, softLogout: true)));
    }
}
