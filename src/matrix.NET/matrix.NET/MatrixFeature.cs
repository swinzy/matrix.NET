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

    /// <summary>Refreshing access tokens with <c>POST /refresh</c>.</summary>
    public static readonly MatrixFeature TokenRefresh = new("Token refresh (POST /refresh)", new MatrixVersion(1, 3));
}
