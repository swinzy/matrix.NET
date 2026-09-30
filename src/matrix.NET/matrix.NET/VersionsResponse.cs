namespace TeamBanana.MatrixDotNet;

/// <summary>
/// The specification versions and experimental features a homeserver supports.
/// </summary>
public class VersionsResponse
{
    /// <summary>
    /// Supported specification versions, in the form <c>vX.Y</c>, or <c>rX.Y.Z</c> for historical
    /// versions.
    /// </summary>
    public required List<string> Versions { get; set; }

    /// <summary>
    /// Experimental features and whether each is supported. A feature missing from this map, or
    /// the map being <see langword="null"/>, means it is not supported.
    /// </summary>
    public Dictionary<string, bool>? UnstableFeatures { get; set; }
}