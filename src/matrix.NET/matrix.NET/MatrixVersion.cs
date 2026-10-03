namespace TeamBanana.MatrixDotNet;

/// <summary>
/// A version of the Matrix specification, e.g. v1.15. Versions compare by major, then minor number.
/// </summary>
/// <param name="Major">The major version number.</param>
/// <param name="Minor">The minor version number.</param>
public readonly record struct MatrixVersion(int Major, int Minor) : IComparable<MatrixVersion>
{
    /// <summary>The oldest version this library supports (D24).</summary>
    public static readonly MatrixVersion Minimum = new(1, 1);

    /// <summary>The major version number.</summary>
    public int Major { get; } = Major >= 0 ? Major : throw new ArgumentOutOfRangeException(nameof(Major));

    /// <summary>The minor version number.</summary>
    public int Minor { get; } = Minor >= 0 ? Minor : throw new ArgumentOutOfRangeException(nameof(Minor));

    /// <inheritdoc/>
    public int CompareTo(MatrixVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);

    /// <summary>Formats the version as the spec writes it, e.g. <c>v1.15</c>.</summary>
    public override string ToString() => $"v{Major}.{Minor}";

    /// <summary>Whether <paramref name="left"/> is older than <paramref name="right"/>.</summary>
    public static bool operator <(MatrixVersion left, MatrixVersion right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> is newer than <paramref name="right"/>.</summary>
    public static bool operator >(MatrixVersion left, MatrixVersion right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> is older than or equal to <paramref name="right"/>.</summary>
    public static bool operator <=(MatrixVersion left, MatrixVersion right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> is newer than or equal to <paramref name="right"/>.</summary>
    public static bool operator >=(MatrixVersion left, MatrixVersion right) => left.CompareTo(right) >= 0;
}
