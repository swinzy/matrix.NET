using System.Globalization;

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

    /// <summary>
    /// Parses a version as <c>GET /versions</c> lists it: <c>vX.Y</c>, e.g. <c>v1.15</c>, or a historical
    /// <c>rX.Y.Z</c> release, which counts as v1.0 (D26).
    /// </summary>
    /// <returns><see langword="false"/> for anything else, which callers should ignore.</returns>
    public static bool TryParse(string? text, out MatrixVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(text) || text.Length < 2)
            return false;

        if (text[0] == 'r')
        {
            // The legacy scheme had no counterpart to minor versions; every r0 release predates v1.1
            if (!text.AsSpan(1).ContainsAnyExcept("0123456789.") && char.IsAsciiDigit(text[1]))
            {
                version = new MatrixVersion(1, 0);
                return true;
            }

            return false;
        }

        if (text[0] != 'v')
            return false;

        var parts = text.AsSpan(1);
        var dot = parts.IndexOf('.');
        if (dot < 0 ||
            !int.TryParse(parts[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
            return false;

        version = new MatrixVersion(major, minor);
        return true;
    }

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
