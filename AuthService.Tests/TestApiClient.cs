using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Controllers.Auth;
using AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuthService.Tests;

internal static class TestApiClient
{
    /// <summary>Code do sistema usado por padrão nos testes de Auth (authenticator é o único sistema seedado).</summary>
    internal const string DefaultSystemCode = "authenticator";

    private static string ResolveRootCredential()
    {
        var value = Environment.GetEnvironmentVariable(WebAppFactory.RootCredentialEnvVar);
        return string.IsNullOrWhiteSpace(value) ? WebAppFactory.RootCredentialDefault : value;
    }

    /// <summary>Credencial do usuário root visível para testes que precisam fazer login com ele.</summary>
    internal static string RootCredential => ResolveRootCredential();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal sealed class LoginDto
    {
        public string Token { get; set; } = string.Empty;
    }

    /// <summary>
    /// Recupera o systemId de um sistema do catálogo oficial pelo Code (lança se não existir).
    /// </summary>
    internal static async Task<Guid> GetSystemIdAsync(WebAppFactory factory, string systemCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Systems.AsNoTracking()
            .Where(s => s.Code == systemCode)
            .Select(s => s.Id)
            .SingleAsync();
    }

    /// <summary>Cliente HTTP com JWT do usuário root seedado (todas as permissões oficiais), atrelado ao sistema authenticator e com header X-System-Id já configurado.</summary>
    internal static async Task<HttpClient> CreateAuthenticatedAsync(WebAppFactory factory)
        => await CreateAuthenticatedAsync(factory, DefaultSystemCode);

    /// <summary>Cliente HTTP com JWT do usuário root seedado, atrelado ao sistema indicado por <paramref name="systemCode"/> e com header X-System-Id já configurado.</summary>
    internal static async Task<HttpClient> CreateAuthenticatedAsync(WebAppFactory factory, string systemCode)
    {
        var systemId = await GetSystemIdAsync(factory, systemCode);
        var client = factory.CreateApiClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new
            {
                email = RootUserSeeder.RootEmail,
                password = ResolveRootCredential(),
                systemId
            },
            JsonOptions);
        login.EnsureSuccessStatusCode();
        var dto = await login.Content.ReadFromJsonAsync<LoginDto>(JsonOptions);
        Assert.NotNull(dto);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", dto.Token);
        client.DefaultRequestHeaders.Add(AuthController.SystemIdHeader, systemId.ToString("D"));
        return client;
    }
}
