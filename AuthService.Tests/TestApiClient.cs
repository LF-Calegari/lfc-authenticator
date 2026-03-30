using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Data;
using Xunit;

namespace AuthService.Tests;

internal static class TestApiClient
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal sealed class LoginDto
    {
        public string Token { get; set; } = string.Empty;
    }

    /// <summary>Cliente HTTP com JWT do usuário bootstrap (todas as permissões oficiais).</summary>
    internal static async Task<HttpClient> CreateAuthenticatedAsync(WebAppFactory factory)
    {
        var client = factory.CreateApiClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = IntegrationBootstrapSeeder.Email, password = IntegrationBootstrapSeeder.Password },
            JsonOptions);
        login.EnsureSuccessStatusCode();
        var dto = await login.Content.ReadFromJsonAsync<LoginDto>(JsonOptions);
        Assert.NotNull(dto);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", dto.Token);
        return client;
    }
}
