using Microsoft.Extensions.Configuration;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Settings for integration tests, read from a JSON file and then from MATRIX_TEST_* environment
/// variables, which take precedence. <c>MATRIX_TEST_TARGET</c> picks the file:
/// <list type="bullet">
/// <item><c>local</c> (default): testsettings.local.json, written by <c>tools/synapse/synapse.sh start</c>
/// for a local Synapse without rate limits.</item>
/// <item><c>remote</c>: testsettings.json, your own homeserver, whose rate limits apply.</item>
/// </list>
/// </summary>
public class TestSettings
{
    public const string NotConfiguredMessage =
        "No homeserver configured: run tools/synapse/synapse.sh start, or see testsettings.example.json";

    public string? Homeserver { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }

    /// <summary>
    /// Optional. When set, logins reuse this device instead of creating a new one each run.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>Whether the tests target the local Synapse rather than your own homeserver.</summary>
    public bool IsLocal { get; private set; }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(Homeserver) && !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Password);

    public static TestSettings Load()
    {
        // The local file is read where the script writes it, not copied to the output, so restarting
        // Synapse with another version takes effect without a rebuild
        var isLocal = Environment.GetEnvironmentVariable("MATRIX_TEST_TARGET")?.ToLowerInvariant() switch
        {
            null or "" or "local" => true,
            "remote" => false,
            var target => throw new InvalidOperationException(
                $"MATRIX_TEST_TARGET must be 'local' or 'remote', not '{target}'.")
        };
        var file = isLocal
            ? FindUpwards("testsettings.local.json")
            : Path.Combine(AppContext.BaseDirectory, "testsettings.json");

        var settings = new TestSettings { IsLocal = isLocal };
        var builder = new ConfigurationBuilder();
        if (file is not null)
            builder.AddJsonFile(file, optional: true);
        builder
            .AddEnvironmentVariables("MATRIX_TEST_")
            .Build()
            .Bind(settings);
        return settings;
    }

    private static string? FindUpwards(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, fileName);
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}