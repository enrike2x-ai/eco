using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;

namespace EconodatMcpServer.Tools;

/// <summary>
/// Tools de solo lectura sobre la base de datos db_econo. Cada método valida
/// su entrada antes de tocar la base de datos; la defensa real de "solo
/// lectura" es el login de SQL Server (ver scripts/create_readonly_login.sql),
/// que no debe tener permisos de escritura aunque este código tenga un bug.
/// </summary>
[McpServerToolType]
public static class EconodatTools
{
    private const int MaxFilas = 200;
    private const int TimeoutSegundos = 15;

    // Bloquea cualquier cosa que no sea un único SELECT: sentencias múltiples,
    // DML/DDL, y llamadas a procedimientos.
    private static readonly Regex PalabrasProhibidas = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|EXEC|EXECUTE|MERGE|TRUNCATE|CREATE|GRANT|REVOKE|sp_|xp_)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string ConnectionString => EconodatConnection.BuildConnectionString();

    [McpServerTool, Description("Lista las tablas disponibles en la base de datos db_econo, con su esquema.")]
    public static async Task<string> ListarTablas()
    {
        const string sql = @"
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME";

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = TimeoutSegundos };
        await using var reader = await cmd.ExecuteReaderAsync();

        var filas = new List<string>();
        while (await reader.ReadAsync() && filas.Count < 2000)
        {
            filas.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        }
        return string.Join("\n", filas);
    }

    [McpServerTool, Description("Describe las columnas (nombre, tipo, nullable) de una tabla puntual de db_econo.")]
    public static async Task<string> DescribirTabla(
        [Description("Nombre de la tabla, ej. OrdenServicio")] string tabla,
        [Description("Esquema de la tabla, por defecto 'dbo'")] string esquema = "dbo")
    {
        const string sql = @"
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @esquema AND TABLE_NAME = @tabla
            ORDER BY ORDINAL_POSITION";

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = TimeoutSegundos };
        cmd.Parameters.AddWithValue("@esquema", esquema);
        cmd.Parameters.AddWithValue("@tabla", tabla);
        await using var reader = await cmd.ExecuteReaderAsync();

        var filas = new List<string>();
        while (await reader.ReadAsync())
        {
            var largo = reader.IsDBNull(3) ? "" : $"({reader.GetInt32(3)})";
            filas.Add($"{reader.GetString(0)} {reader.GetString(1)}{largo} NULL={reader.GetString(2)}");
        }
        return filas.Count == 0
            ? $"No se encontró la tabla {esquema}.{tabla} (o el login no tiene permiso de lectura sobre ella)."
            : string.Join("\n", filas);
    }

    [McpServerTool, Description(
        "Ejecuta una única consulta SELECT de solo lectura contra db_econo. " +
        "No admite INSERT/UPDATE/DELETE/DROP/ALTER/EXEC ni múltiples sentencias. " +
        "Devuelve como máximo 200 filas.")]
    public static async Task<string> EjecutarConsultaSelect(
        [Description("Consulta SQL, debe empezar con SELECT")] string sql)
    {
        var consulta = sql.Trim().TrimEnd(';');

        if (!Regex.IsMatch(consulta, @"^\s*SELECT\b", RegexOptions.IgnoreCase))
            return "Rechazado: la consulta debe comenzar con SELECT.";

        if (consulta.Contains(';'))
            return "Rechazado: no se permite más de una sentencia por llamada.";

        if (PalabrasProhibidas.IsMatch(consulta))
            return "Rechazado: la consulta contiene una palabra clave no permitida en este modo de solo lectura.";

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(consulta, conn) { CommandTimeout = TimeoutSegundos };
        await using var reader = await cmd.ExecuteReaderAsync();

        var columnas = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToArray();

        var filas = new List<string> { string.Join(" | ", columnas) };
        var truncado = false;
        while (await reader.ReadAsync())
        {
            if (filas.Count - 1 >= MaxFilas)
            {
                truncado = true;
                break;
            }
            var valores = Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "");
            filas.Add(string.Join(" | ", valores));
        }

        if (truncado)
            filas.Add($"[... resultado truncado a {MaxFilas} filas, refina la consulta ...]");

        return string.Join("\n", filas);
    }
}
