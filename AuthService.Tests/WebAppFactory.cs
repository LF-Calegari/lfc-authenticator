using AuthService.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AuthService.Tests;

/// <summary>
/// Sobe a API com SQL Server real: cria um banco com nome único e remove ao descartar o factory.
/// Um factory por teste permite paralelismo sem colisão (cada um usa seu próprio database).
/// </summary>
public class WebAppFactory : WebApplicationFactory<Program>
{
    private const string TestSqlBaseEnv = "AUTH_SERVICE_TEST_SQL_BASE";

    private readonly string _databaseName;
    private readonly string _masterConnectionString;
    private readonly string _appConnectionString;
    private bool _disposed;

    public WebAppFactory()
    {
        var baseConnection = Environment.GetEnvironmentVariable(TestSqlBaseEnv)?.Trim();
        if (string.IsNullOrEmpty(baseConnection))
        {
            throw new InvalidOperationException(
                $"Defina a variável de ambiente {TestSqlBaseEnv} com a connection string do SQL Server " +
                "(sem Initial Catalog / Database), por exemplo: " +
                "Server=127.0.0.1,1433;User Id=sa;Password=...;TrustServerCertificate=True");
        }

        _databaseName = "auth_svc_it_" + Guid.NewGuid().ToString("N");
        _masterConnectionString = SqlServerTestDb.BuildMasterConnectionString(baseConnection);
        _appConnectionString = SqlServerTestDb.BuildDatabaseConnectionString(baseConnection, _databaseName);

        SqlServerTestDb.CreateDatabase(_masterConnectionString, _databaseName);
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
        return host;
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
                    SqlServerTestDb.DropDatabase(_masterConnectionString, _databaseName);
                }
                catch
                {
                    // Não oculta falhas do teste; drop pode falhar se o SQL não estiver acessível.
                }
            }

            return;
        }

        base.Dispose(disposing);
    }
}
