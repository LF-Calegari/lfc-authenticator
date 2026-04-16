using Npgsql;

namespace AuthService.Tests;

/// <summary>
/// Cria/remove bancos dedicados para testes de integração (um nome por instância do factory).
/// </summary>
internal static class PostgreSqlTestDb
{
    public static string BuildAdminConnectionString(string baseConnection)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            Database = "postgres"
        };
        return builder.ConnectionString;
    }

    public static string BuildDatabaseConnectionString(string baseConnection, string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }

    public static void CreateDatabase(string adminConnectionString, string databaseName)
    {
        using var conn = new NpgsqlConnection(adminConnectionString);
        conn.Open();
        var safe = databaseName.Replace("\"", "\"\"");
        using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{safe}\"", conn);
        cmd.ExecuteNonQuery();
    }

    public static void DropDatabase(string adminConnectionString, string databaseName)
    {
        NpgsqlConnection.ClearAllPools();

        using var conn = new NpgsqlConnection(adminConnectionString);
        conn.Open();
        var safe = databaseName.Replace("\"", "\"\"");
        using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{safe}\" WITH (FORCE)", conn);
        drop.ExecuteNonQuery();
    }
}
