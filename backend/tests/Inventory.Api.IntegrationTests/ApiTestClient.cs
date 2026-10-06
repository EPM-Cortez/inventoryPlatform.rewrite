using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Contracts.V1;
using System.Net.Http.Json;

namespace Inventory.Api.IntegrationTests;

internal static class ApiTestClient
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(JsonOptions);
        return token?.Token
            ?? throw new InvalidOperationException("The test server did not return a CSRF token.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        return options;
    }
}
