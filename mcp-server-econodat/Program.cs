using EconodatMcpServer;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET Core lee automáticamente variables de entorno (ECONODAT_API_KEY,
// DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD) vía IConfiguration.

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/health", () => Results.Ok("healthy"));
app.MapMcp("/mcp");

app.Run();
