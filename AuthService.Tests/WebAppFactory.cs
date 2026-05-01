using AuthService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AuthService.Tests;

/// <summary>
/// Sobe a API com PostgreSQL real: cria um banco com nome único e remove ao descartar o factory.
/// Um factory por teste permite paralelismo sem colisão (cada um usa seu próprio database).
/// </summary>
public class WebAppFactory : WebApplicationFactory<Program>
{
    private const string TestPgBaseEnv = "AUTH_SERVICE_TEST_PG_BASE";

    /// <summary>Variável de ambiente esperada por <see cref="RootUserSeeder"/> para a senha do root.</summary>
    public const string RootCredentialEnvVar = "DEFAULT_SYSTEM_USER_PASSWORD";

    /// <summary>Senha default do root quando a env var não está definida (apenas em ambiente de teste).</summary>
    public const string RootCredentialDefault = "toor";

    private readonly string _databaseName;
    private readonly string _masterConnectionString;
    private readonly string _appConnectionString;
    private bool _disposed;

    public WebAppFactory()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RootCredentialEnvVar)))
            Environment.SetEnvironmentVariable(RootCredentialEnvVar, RootCredentialDefault);

        var baseConnection = Environment.GetEnvironmentVariable(TestPgBaseEnv)?.Trim();
        if (string.IsNullOrEmpty(baseConnection))
        {
            throw new InvalidOperationException(
                $"Defina a variável de ambiente {TestPgBaseEnv} com a connection string do PostgreSQL " +
                "sem Database (usa-se o banco de manutenção internamente), por exemplo: " +
                "Host=127.0.0.1;Port=5432;Username=auth;Password=...");
        }

        _databaseName = "auth_svc_it_" + Guid.NewGuid().ToString("N");
        _masterConnectionString = PostgreSqlTestDb.BuildAdminConnectionString(baseConnection);
        _appConnectionString = PostgreSqlTestDb.BuildDatabaseConnectionString(baseConnection, _databaseName);

        PostgreSqlTestDb.CreateDatabase(_masterConnectionString, _databaseName);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _appConnectionString,
                ["Auth:Jwt:Secret"] = "integration-tests-jwt-secret-key-32chars!!",
                ["Auth:Jwt:ExpirationMinutes"] = "60"
            });
        });

        var interceptors = AdditionalDbInterceptors;
        if (interceptors.Count > 0)
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(_appConnectionString)
                        .AddInterceptors(interceptors));
            });
        }
    }

    /// <summary>
    /// Hook para subclasses registrarem <see cref="IInterceptor"/>s no <see cref="AppDbContext"/> de teste
    /// (ex.: contadores de comandos SQL). Lista vazia por padrão — o factory base não muda comportamento.
    /// </summary>
    protected virtual IReadOnlyList<IInterceptor> AdditionalDbInterceptors { get; } = Array.Empty<IInterceptor>();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();

        // Em "Testing" o Program.cs não roda os seeders — replicamos a mesma sequência usada em produção
        // para que os testes de integração tenham o catálogo do authenticator e o usuário root prontos.
        SystemSeeder.EnsureSystemsAsync(db).GetAwaiter().GetResult();
        SystemTokenTypeSeeder.EnsureSystemTokenTypesAsync(db).GetAwaiter().GetResult();
        AuthenticatorRoutesSeeder.EnsureRoutesAsync(db).GetAwaiter().GetResult();
        PermissionTypeSeeder.EnsurePermissionTypesAsync(db).GetAwaiter().GetResult();
        AuthenticatorPermissionsSeeder.EnsurePermissionsAsync(db).GetAwaiter().GetResult();
        RootUserSeeder.EnsureRootUserAsync(db).GetAwaiter().GetResult();
        RootRolePermissionsSeeder.EnsureRootRolePermissionsAsync(db).GetAwaiter().GetResult();

        return host;
    }

    public HttpClient CreateApiClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = Server.BaseAddress
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            try
            {
                base.Dispose(true);
            }
            finally
            {
                try
                {
                    PostgreSqlTestDb.DropDatabase(_masterConnectionString, _databaseName);
                }
                catch
                {
                    // Não oculta falhas do teste; drop pode falhar se o PostgreSQL não estiver acessível.
                }
            }

            return;
        }

        base.Dispose(disposing);
    }

}
