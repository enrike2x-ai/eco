using Microsoft.Data.SqlClient;

namespace EconodatMcpServer.Tools;

/// <summary>
/// Construye la connection string a partir de variables de entorno.
/// Nunca hardcodear host/usuario/password aquí: se configuran en el
/// Application Pool de IIS o en web.config (ver README.md).
/// </summary>
public static class EconodatConnection
{
    public static string BuildConnectionString()
    {
        var host = RequireEnv("DB_HOST");
        var port = Environment.GetEnvironmentVariable("DB_PORT") is { Length: > 0 } p ? p : "1433";
        var db = RequireEnv("DB_NAME");
        var user = RequireEnv("DB_USER");
        var password = RequireEnv("DB_PASSWORD");

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{host},{port}",
            InitialCatalog = db,
            UserID = user,
            Password = password,
            Encrypt = true,
            TrustServerCertificate = false,
            ConnectTimeout = 10,
        };
        return builder.ConnectionString;
    }

    private static string RequireEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException(
            $"Falta la variable de entorno {name}. Configúrala en el Application Pool de IIS.");
}
