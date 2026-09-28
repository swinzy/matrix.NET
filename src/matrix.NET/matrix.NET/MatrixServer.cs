using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamBanana.MatrixDotNet;

public class MatrixServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _client;

    public MatrixServer(Uri baseUri)
        : this(new HttpClient { BaseAddress = baseUri })
    {
    }

    public MatrixServer(HttpClient client)
    {
        _client = client;
    }

    public async Task<List<LoginFlow>> GetSupportedLoginTypesAsync()
    {
        var response = await _client.GetAsync("/_matrix/client/v3/login");
        await EnsureSuccessAsync(response);
        var flows = await response.Content.ReadFromJsonAsync<LoginFlowsResponse>(JsonOptions);
        return flows?.Flows ?? [];
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var response = await _client.PostAsJsonAsync("/_matrix/client/v3/login", request, JsonOptions);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        ErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonOptions);
        }
        catch (JsonException)
        {
            // Not a Matrix error body, e.g. an HTML page from a reverse proxy
        }

        throw new MatrixException(response.StatusCode, error?.Errcode ?? "M_UNKNOWN", error?.Error);
    }

    private record ErrorResponse(string Errcode, string? Error);

    private record LoginFlowsResponse(List<LoginFlow>? Flows);
}