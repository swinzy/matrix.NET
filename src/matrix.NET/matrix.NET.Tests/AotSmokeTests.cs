using System.Diagnostics;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Publishes matrix.NET.AotSmoke with Native AOT and runs it (D29). Publishing fails on any trimming
/// or AOT warning in the library; running checks the serialisation paths in native code, against
/// the local Synapse when it is configured.
/// </summary>
public class AotSmokeTests
{
    // Explicit: compiles to native code, which takes a while and needs clang
    [Fact(Explicit = true)]
    public async Task PublishedApp_PassesItsChecks()
    {
        var project = FindProject();
        var output = Path.Combine(Path.GetDirectoryName(project)!, "bin", "aot-smoke");

        var publish = await RunAsync("dotnet", ["publish", project, "-c", "Release", "-o", output], new());
        Assert.True(publish.ExitCode == 0, $"dotnet publish failed:{Environment.NewLine}{publish.Output}");

        // Only the local Synapse: the checks log in several times and expect 2-second tokens
        var settings = TestSettings.Load();
        var environment = new Dictionary<string, string>();
        if (settings is { IsLocal: true, IsConfigured: true })
        {
            environment["MATRIX_TEST_HOMESERVER"] = settings.Homeserver!;
            environment["MATRIX_TEST_USER"] = settings.User!;
            environment["MATRIX_TEST_PASSWORD"] = settings.Password!;
        }

        var run = await RunAsync(Path.Combine(output, "matrix.NET.AotSmoke"), [], environment);
        Assert.True(run.ExitCode == 0, $"The native app reported failures:{Environment.NewLine}{run.Output}");
    }

    private static string FindProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "matrix.NET.AotSmoke", "matrix.NET.AotSmoke.csproj");
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("matrix.NET.AotSmoke.csproj not found above the test output.");
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string fileName, string[] arguments,
        Dictionary<string, string> environment)
    {
        var start = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var (name, value) in environment)
            start.Environment[name] = value;

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await stdout + await stderr);
    }
}
