using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TeamBanana.MatrixDotNet.Tests;

public class CreateRoomTests
{
    private static readonly MatrixSession Session = new()
    {
        Homeserver = new Uri("https://matrix.example.org/"),
        UserId = "@joe:example.org",
        DeviceId = "ABC1234",
        AccessToken = "secret-access"
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StubHttpMessageHandler Created() =>
        new(HttpStatusCode.OK, """{"room_id":"!new:example.org"}""");

    private static MatrixClient CreateClient(StubHttpMessageHandler handler) => new(Session, new HttpClient(handler));

    [Fact]
    public async Task CreateRoomAsync_WithoutParametersSendsAnEmptyObject()
    {
        var handler = Created();

        var roomId = await CreateClient(handler).CreateRoomAsync(cancellationToken: Ct);

        Assert.Equal("!new:example.org", roomId);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://matrix.example.org/_matrix/client/v3/createRoom", handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal("secret-access", handler.Request.Headers.Authorization!.Parameter);
        Assert.Equal("{}", handler.RequestBody);
    }

    [Fact]
    public async Task CreateRoomAsync_SendsEveryParameterAsTheSpecNamesIt()
    {
        var handler = Created();

        await CreateClient(handler).CreateRoomAsync(new CreateRoomRequest
        {
            Preset = RoomPresets.TrustedPrivateChat,
            Visibility = RoomVisibilities.Private,
            Name = "Café ☕",
            Topic = "Plans",
            RoomAliasName = "plans",
            Invite = ["@alice:example.org"],
            IsDirect = true,
            RoomVersion = "12",
            CreationContent = new JsonObject { ["m.federate"] = false },
            InitialState =
            [
                new InitialStateEvent
                {
                    Type = "m.room.guest_access", Content = new JsonObject { ["guest_access"] = "forbidden" }
                }
            ],
            PowerLevelContentOverride = new JsonObject { ["events_default"] = 50 },
            AdditionalProperties = new Dictionary<string, JsonElement>
            {
                ["invite_3pid"] = JsonElement.Parse("[]")
            }
        }, cancellationToken: Ct);

        const string expected =
            """
            {"preset":"trusted_private_chat","visibility":"private","name":"Café ☕","topic":"Plans","room_alias_name":"plans","invite":["@alice:example.org"],"is_direct":true,"room_version":"12","creation_content":{"m.federate":false},"initial_state":[{"type":"m.room.guest_access","state_key":"","content":{"guest_access":"forbidden"}}],"power_level_content_override":{"events_default":50},"invite_3pid":[]}
            """;
        Assert.Equal(expected, handler.RequestBody);
    }

    [Theory]
    [InlineData("#plans:example.org")]
    [InlineData("#plans")]
    [InlineData("plans:example.org")]
    [InlineData("")]
    public async Task CreateRoomAsync_RejectsAFullAliasAndSendsNothing(string aliasName)
    {
        var handler = Created();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateClient(handler).CreateRoomAsync(new CreateRoomRequest { RoomAliasName = aliasName }, cancellationToken: Ct));

        Assert.Contains("local part", exception.Message);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task CreateRoomAsync_RejectsIncompleteInvitesAndStateEvents()
    {
        var handler = Created();
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CreateRoomAsync(new CreateRoomRequest { Invite = ["@alice:example.org", ""] }, cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CreateRoomAsync(new CreateRoomRequest
            {
                InitialState = [new InitialStateEvent { Type = "", Content = new JsonObject() }]
            }, cancellationToken: Ct));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.CreateRoomAsync(new CreateRoomRequest { InitialState = [null!] }, cancellationToken: Ct));

        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task CreateRoomAsync_ThrowsTheHomeserversRefusal()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest,
            """{"errcode":"M_ROOM_IN_USE","error":"Room alias already taken"}""");

        var exception = await Assert.ThrowsAsync<MatrixException>(() =>
            CreateClient(handler).CreateRoomAsync(new CreateRoomRequest { RoomAliasName = "plans" }, cancellationToken: Ct));

        Assert.Equal(MatrixErrorCodes.RoomInUse, exception.ErrorCode);
    }
}
