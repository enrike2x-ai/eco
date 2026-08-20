namespace EconodatMcpServer;

/// <summary>
/// Exige el header "X-Api-Key" en toda request antes de llegar al endpoint MCP.
/// La clave real vive en la variable de entorno ECONODAT_API_KEY, nunca en código.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, IConfiguration config, ILogger<ApiKeyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var expected = config["ECONODAT_API_KEY"];
        if (string.IsNullOrEmpty(expected))
        {
            logger.LogError("ECONODAT_API_KEY no está configurada — rechazando todas las requests.");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Servidor mal configurado: falta ECONODAT_API_KEY.");
            return;
        }

        var provided = context.Request.Headers["X-Api-Key"].ToString();
        if (string.IsNullOrEmpty(provided) || provided != expected)
        {
            logger.LogWarning("Request rechazada por API key inválida desde {Ip}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("API key inválida o ausente.");
            return;
        }

        await next(context);
    }
}
