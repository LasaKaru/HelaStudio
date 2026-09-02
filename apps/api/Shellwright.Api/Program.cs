using System.Text.Json.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using Shellwright.Api.Auth;
using Shellwright.Api.Authorization;
using Shellwright.Api.Config;
using Shellwright.Api.Data;
using Shellwright.Api.Endpoints;
using Shellwright.Api.Observability;
using Shellwright.Api.Problems;

var builder = WebApplication.CreateBuilder(args);

builder.AddShellwrightLogging();

// ⚠️ Enums cross the wire as their names, in both directions.
//
// Serialising a role as 3 would make the API unreadable and would tie every
// client to the numeric order — which is load-bearing internally precisely
// because it can be renumbered. Accepting them as names matters just as much:
// without this converter the API happily *emits* "Owner" and rejects it on the
// way back in with a 400 that says nothing useful.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddShellwrightData(builder.Configuration);
builder.Services.AddShellwrightAuth(builder.Configuration);
builder.Services.AddShellwrightAuthorization();
builder.Services.AddShellwrightConfig(builder.Configuration);
builder.Services.AddShellwrightTelemetry(builder.Configuration);
builder.Services.AddShellwrightRateLimiting();
builder.Services.AddShellwrightProblemDetails();
builder.Services.AddOpenApi("v1", ApiDocument.Configure);

// ⚠️ Deny by default. Without this, an endpoint that nobody remembered to
// decorate is anonymous, and the mistake is invisible in review because the
// missing line is not in the diff. With it, forgetting locks people out
// instead — a bug someone reports in minutes rather than one nobody reports at
// all. EndpointAuthorizationTests asserts the same property from the other
// side, over the whole route table.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

var app = builder.Build();

// ⚠️ Order is the contract here, and each step depends on the one before it.
//
//   correlation   first, so every log line and every failure below carries it
//   exceptions    outside everything that can throw
//   rate limiting before authentication, so an unauthenticated flood is
//                 rejected before it costs an Argon2 verification
//   authentication establishes the subject
//   tenant scope  stamps that subject onto database connections
//   authorisation decides, with both available
app.UseMiddleware<CorrelationMiddleware>();
app.UseExceptionHandler();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantScopeMiddleware>();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapOrgEndpoints();
app.MapApiTokenEndpoints();
app.MapAppEndpoints();
app.MapConfigEndpoints();
app.MapAssetEndpoints();

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

// Writing the document and exiting, rather than serving it, is what lets CI
// regenerate the TypeScript client without starting a server or reaching a
// database.
if (ApiDocument.ShouldExport(args, out var destination))
{
    await ApiDocument.WriteAsync(app, destination);
    return;
}

await app.RunAsync();

/// <summary>Entry point marker so integration tests can boot the real application.</summary>
public partial class Program;
