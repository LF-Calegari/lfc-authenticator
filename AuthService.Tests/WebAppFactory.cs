using AuthService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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

    private readonly string _databaseName;
    private readonly string _masterConnectionString;
    private readonly string _appConnectionString;
    private bool _disposed;

    private const string DefaultUserCredentialEnvVar = "DEFAULT_SYSTEM_USER_PASSWORD";
    private const string DefaultUserCredentialDefault = "toor";
    private const string AdminUserCredentialEnvVar = "ADMIN_SYSTEM_USER_PASSWORD";
    private const string AdminUserCredentialDefault = "admin";
    private const string StandardUserCredentialEnvVar = "DEFAULT_USER_PASSWORD";
    private const string StandardUserCredentialDefault = "default";

    public WebAppFactory()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DefaultUserCredentialEnvVar)))
            Environment.SetEnvironmentVariable(DefaultUserCredentialEnvVar, DefaultUserCredentialDefault);

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AdminUserCredentialEnvVar)))
            Environment.SetEnvironmentVariable(AdminUserCredentialEnvVar, AdminUserCredentialDefault);

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(StandardUserCredentialEnvVar)))
            Environment.SetEnvironmentVariable(StandardUserCredentialEnvVar, StandardUserCredentialDefault);

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
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        OfficialCatalogSeeder.EnsureCatalogAsync(db).GetAwaiter().GetResult();
        KurttoAccessSeeder.EnsureKurttoAccessAsync(db).GetAwaiter().GetResult();
        DefaultSystemUserSeeder.EnsureDefaultUserAsync(db).GetAwaiter().GetResult();
        LegacyUsersClientSeeder.EnsureLegacyUsersHaveClientAsync(db).GetAwaiter().GetResult();
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
