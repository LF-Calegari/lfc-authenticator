using Microsoft.Data.SqlClient;

namespace AuthService.Tests;

/// <summary>
/// Cria/remove bancos dedicados para testes de integração (um nome por instância do factory).
/// </summary>
internal static class SqlServerTestDb
{
    public static string BuildMasterConnectionString(string baseConnection)
    {
        var builder = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = "master"
        };
        return builder.ConnectionString;
    }

    public static string BuildDatabaseConnectionString(string baseConnection, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = databaseName
        };
        return builder.ConnectionString;
    }

    public static void CreateDatabase(string masterConnectionString, string databaseName)
    {
        using var conn = new SqlConnection(masterConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE [{EscapeDbName(databaseName)}]";
        cmd.ExecuteNonQuery();
    }

    public static void DropDatabase(string masterConnectionString, string databaseName)
    {
        SqlConnection.ClearAllPools();

        using var conn = new SqlConnection(masterConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        var safe = EscapeDbName(databaseName);
        cmd.CommandText = $"""
            IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{safe.Replace("'", "''")}')
            BEGIN
                ALTER DATABASE [{safe}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{safe}];
            END
            """;
        cmd.ExecuteNonQuery();
    }

    private static string EscapeDbName(string name) => name.Replace("]", "]]");
}
