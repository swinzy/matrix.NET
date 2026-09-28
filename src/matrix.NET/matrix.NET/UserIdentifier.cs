using System.Text.Json.Serialization;

namespace TeamBanana.MatrixDotNet;

public class UserIdentifier : IIdentifier
{
    public required string User { get; set; }

    [JsonIgnore]
    public IdentifierType Type => IdentifierType.User;
}