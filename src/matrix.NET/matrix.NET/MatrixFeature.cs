namespace TeamBanana.MatrixDotNet;

/// <summary>
/// A feature that only some homeservers support, because it was added to the spec after the
/// oldest version this library supports, or removed from it later (D26). Each feature is one of
/// the static members of this class.
/// </summary>
/// <remarks>
/// Features from <see cref="MatrixVersion.Minimum"/> or earlier have no member: every supported
/// homeserver has them.
/// </remarks>
public sealed class MatrixFeature
{
    private MatrixFeature(string name, MatrixVersion addedIn, MatrixVersion? removedIn = null,
        string? unstableFeatureFlag = null)
    {
        Name = name;
        AddedIn = addedIn;
        RemovedIn = removedIn;
        UnstableFeatureFlag = unstableFeatureFlag;
    }

    /// <summary>A human-readable name, e.g. for error messages.</summary>
    public string Name { get; }

    /// <summary>The spec version that added the feature.</summary>
    public MatrixVersion AddedIn { get; }

    /// <summary>The spec version that removed the feature, or <see langword="null"/> if it has not been removed.</summary>
    public MatrixVersion? RemovedIn { get; }

    /// <summary>
    /// The key in <see cref="VersionsResponse.UnstableFeatures"/> by which a homeserver can advertise
    /// the feature before advertising <see cref="AddedIn"/>, usually its MSC's <c>.stable</c> flag;
    /// <see langword="null"/> if there is none.
    /// </summary>
    public string? UnstableFeatureFlag { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;

    /// <summary>Whether the spec has the feature in <paramref name="version"/>.</summary>
    internal bool IsIn(MatrixVersion version) => version >= AddedIn && (RemovedIn is not { } removed || version < removed);

    /// <summary>
    /// Explains why the homeserver may have answered as if the feature's endpoint did not exist, from
    /// the versions it advertised last, if any are cached (D26). Never certain, as a proxy may answer too.
    /// </summary>
    internal string ExplainMissing(VersionsResponse? versions)
    {
        var required = RemovedIn is { } removedIn
            ? $"Matrix {AddedIn} to versions before {removedIn}"
            : $"Matrix {AddedIn} or later";

        if (versions is null)
            return $"{Name} requires {required}, so the homeserver likely does not support it.";
        if (versions.Supports(this))
            return $"The homeserver advertises {Name}, yet answered as if it did not exist; a likely cause " +
                   "is a reverse proxy that does not forward this path.";

        var advertised = versions.LatestVersion is { } latest
            ? $"advertises up to {latest}"
            : "advertises no version this library recognises";
        return RemovedIn is { } removed && versions.LatestVersion >= removed
            ? $"{Name} was removed in Matrix {removed}, and the homeserver {advertised} without any version " +
              "that still has it, so it likely does not support it."
            : $"{Name} requires {required}, but the homeserver {advertised}, so it likely does not support it.";
    }

    /// <summary>Refreshing access tokens with <c>POST /refresh</c>.</summary>
    public static readonly MatrixFeature TokenRefresh = new("Token refresh (POST /refresh)", new MatrixVersion(1, 3));
}
