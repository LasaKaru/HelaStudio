using Shellwright.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddShellwrightData(builder.Configuration);

var app = builder.Build();

// Liveness answers "is this process running", and nothing else. Anything that
// touches a dependency belongs in readiness: a liveness probe that fails when
// the database blips will restart every healthy instance at once.
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Entry point marker so integration tests can boot the real application.</summary>
public partial class Program;
