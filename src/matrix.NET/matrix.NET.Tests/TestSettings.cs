using Microsoft.Extensions.Configuration;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Settings for tests against a real homeserver, read from testsettings.json
/// and then from MATRIX_TEST_* environment variables (which take precedence).
/// </summary>
public class TestSettings
{
    public string? Homeserver { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }

    /// <summary>
    /// Optional. When set, logins reuse this device instead of creating a new one each run.
    /// </summary>
    public string? DeviceId { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(Homeserver) && !string.IsNullOrEmpty(User) && !string.IsNullOrEmpty(Password);

    public static TestSettings Load()
    {
        var settings = new TestSettings();
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("testsettings.json", optional: true)
            .AddEnvironmentVariables("MATRIX_TEST_")
            .Build()
            .Bind(settings);
        return settings;
    }
}