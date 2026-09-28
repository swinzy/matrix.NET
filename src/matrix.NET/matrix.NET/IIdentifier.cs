using System.Text.Json.Serialization;

namespace TeamBanana.MatrixDotNet;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(UserIdentifier), "m.id.user")]
public interface IIdentifier
{
    public IdentifierType Type { get; }
}

public enum IdentifierType
{
    User, ThirdParty, Phone
}