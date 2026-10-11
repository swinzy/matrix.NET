using System.Text.Json.Serialization;

namespace TeamBanana.MatrixDotNet;

/// <summary>
/// The specification versions and experimental features a homeserver supports.
/// </summary>
/// <remarks>
/// Read-only, because <see cref="MatrixServer"/> and <see cref="MatrixClient"/> cache it and hand the
/// same instance to every caller (D25).
/// </remarks>
public sealed class VersionsResponse
{
    private IReadOnlyList<MatrixVersion>? _specVersions;

    /// <summary>
    /// Supported specification versions, in the form <c>vX.Y</c>, or <c>rX.Y.Z</c> for historical
    /// versions.
    /// </summary>
    public required IReadOnlyList<string> Versions { get; init; }

    /// <summary>
    /// Experimental features and whether each is supported. A feature missing from this map, or
    /// the map being <see langword="null"/>, means it is not supported.
    /// </summary>
    public IReadOnlyDictionary<string, bool>? UnstableFeatures { get; init; }

    /// <summary>
    /// <see cref="Versions"/> as comparable versions, skipping any this library cannot read;
    /// historical <c>rX.Y.Z</c> releases count as v1.0.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<MatrixVersion> SpecVersions => _specVersions ??=
    [
        ..Versions.Select(text => MatrixVersion.TryParse(text, out var version) ? (MatrixVersion?)version : null)
            .OfType<MatrixVersion>()
    ];

    /// <summary>The newest version advertised, or <see langword="null"/> if none could be read.</summary>
    [JsonIgnore]
    public MatrixVersion? LatestVersion => SpecVersions.Count == 0 ? null : SpecVersions.Max();

    /// <summary>
    /// Whether the homeserver advertises <see cref="MatrixVersion.Minimum"/> or later, the oldest version
    /// this library supports (D24). Apps can check it before logging in, e.g. on a login screen.
    /// </summary>
    [JsonIgnore]
    public bool MeetsMinimumVersion => SpecVersions.Any(version => version >= MatrixVersion.Minimum);

    /// <summary>
    /// Whether the homeserver advertises <paramref name="feature"/>: some advertised version has it, or
    /// its <see cref="MatrixFeature.UnstableFeatureFlag"/> is <see langword="true"/> (D26).
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> is not proof that the feature is missing, as a homeserver may ship it
    /// before advertising the version that contains it.
    /// </remarks>
    public bool Supports(MatrixFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (feature.UnstableFeatureFlag is { } flag && UnstableFeatures?.GetValueOrDefault(flag) == true)
            return true;

        return SpecVersions.Any(feature.IsIn);
    }
}
