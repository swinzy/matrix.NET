namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Checks rules in the library's source that the compiler and other tests cannot see. Explicit:
/// they only change when the source does, so run them before releases and after larger changes.
/// </summary>
public class SourceRulesTests
{
    // MatrixJsonContext.Default lacks relaxed escaping; using it would still produce valid JSON, so
    // no other test would notice (D31)
    [Fact(Explicit = true)]
    public void Library_UsesMatrixJsonContextInstanceNotDefault()
    {
        var offenders = LibrarySourceFiles()
            .Where(file => Path.GetFileName(file) != "MatrixJsonContext.cs")
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(entry => entry.line.Contains("MatrixJsonContext.Default"))
            .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Use MatrixJsonContext.Instance instead:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> LibrarySourceFiles()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var library = Path.Combine(directory.FullName, "matrix.NET");
            if (File.Exists(Path.Combine(library, "matrix.NET.csproj")))
                return Directory.EnumerateFiles(library, "*.cs", SearchOption.AllDirectories)
                    .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                                   !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
        }

        throw new DirectoryNotFoundException("The library's source was not found above the test output.");
    }
}
