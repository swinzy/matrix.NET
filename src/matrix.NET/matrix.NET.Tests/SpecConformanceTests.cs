using System.Reflection;
using System.Text.Json;
using TeamBanana.MatrixDotNet.Transport;

namespace TeamBanana.MatrixDotNet.Tests;

/// <summary>
/// Checks every declared endpoint against the spec, so a missing or wrong <see cref="MatrixFeature"/>
/// or authentication requirement is caught (D26).
/// </summary>
public class SpecConformanceTests
{
    // The spec baseline from docs/technical.md; update both together
    private const string SpecVersion = "v1.19";
    private static readonly MatrixVersion SpecBaseline = new(1, 19);

    private static IReadOnlyList<(string Name, Endpoint Endpoint)> DeclaredEndpoints() =>
        typeof(Endpoints).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(Endpoint))
            .Select(field => (field.Name, (Endpoint)field.GetValue(null)!))
            .ToList();

    [Fact]
    public void Endpoints_AreDeclaredOnceEach()
    {
        var endpoints = DeclaredEndpoints();

        Assert.NotEmpty(endpoints);
        Assert.Equal(endpoints.Count, endpoints.Select(e => (e.Endpoint.Method, e.Endpoint.Path)).Distinct().Count());
    }

    // Downloads the spec's OpenAPI definition, so it needs the network
    [Fact(Explicit = true)]
    public async Task Endpoints_MatchTheSpec()
    {
        using var http = new HttpClient();
        await using var stream = await http.GetStreamAsync(
            $"https://spec.matrix.org/{SpecVersion}/client-server-api/api.json", TestContext.Current.CancellationToken);
        using var spec = await JsonDocument.ParseAsync(stream, cancellationToken: TestContext.Current.CancellationToken);
        var paths = spec.RootElement.GetProperty("paths");

        var problems = new List<string>();
        foreach (var (name, endpoint) in DeclaredEndpoints())
        {
            var feature = endpoint.Feature;
            var found = TryGetOperation(paths, endpoint, out var operation);

            if (feature?.RemovedIn is { } removedIn && removedIn <= SpecBaseline)
            {
                if (found)
                    problems.Add($"{name}: declared removed in {removedIn}, but the spec still has {endpoint}.");
                continue;
            }

            if (!found)
            {
                problems.Add($"{name}: the spec has no {endpoint}. Check the path, or declare when it was removed.");
                continue;
            }

            var specAddedIn = operation.TryGetProperty("x-addedInMatrixVersion", out var added)
                ? ParseSpecVersion(added.GetString()!)
                : (MatrixVersion?)null;
            if (specAddedIn > MatrixVersion.Minimum)
            {
                if (feature is null)
                    problems.Add($"{name}: added in {specAddedIn}, after the minimum; declare it with Endpoint.Since.");
                else if (feature.AddedIn != specAddedIn)
                    problems.Add($"{name}: {feature.Name} says added in {feature.AddedIn}, the spec says {specAddedIn}.");
            }
            else if (feature is not null && feature.AddedIn > MatrixVersion.Minimum)
            {
                problems.Add($"{name}: {feature.Name} says added in {feature.AddedIn}, but the spec has it from the minimum.");
            }

            if (ExpectedAuth(operation) is { } auth && auth != endpoint.Auth)
                problems.Add($"{name}: declared {endpoint.Auth} authentication, the spec says {auth}.");
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static bool TryGetOperation(JsonElement paths, Endpoint endpoint, out JsonElement operation)
    {
        operation = default;
        return paths.TryGetProperty("/" + endpoint.Path, out var operations) &&
               operations.TryGetProperty(endpoint.Method.Method.ToLowerInvariant(), out operation);
    }

    // The spec writes versions without the "v", e.g. "1.15"
    private static MatrixVersion ParseSpecVersion(string version)
    {
        var parts = version.Split('.');
        return new MatrixVersion(int.Parse(parts[0]), int.Parse(parts[1]));
    }

    // No security: no token. An empty alternative: a token is optional. Otherwise a user token is required.
    // Null for schemes this library does not use, e.g. application service tokens
    private static AuthRequirement? ExpectedAuth(JsonElement operation)
    {
        if (!operation.TryGetProperty("security", out var security))
            return AuthRequirement.None;

        var schemes = security.EnumerateArray().Select(alternative => alternative.EnumerateObject().Select(p => p.Name).ToList()).ToList();
        if (schemes.Any(s => s.Count == 0))
            return AuthRequirement.Optional;
        return schemes.Any(s => s.Contains("accessTokenBearer")) ? AuthRequirement.Required : null;
    }
}
