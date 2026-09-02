using System.Text.Json.Serialization;
using Shellwright.Api.Auth;
using Shellwright.Api.Authorization;
using Shellwright.Api.Data;
using Shellwright.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

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

app.UseAuthentication();

// Between authentication and authorisation: the subject has been established,
// and nothing has opened a database connection yet.
app.UseMiddleware<TenantScopeMiddleware>();

app.UseAuthorization();

// Liveness answers "is this process running", and nothing else. Anything that
// touches a dependency belongs in readiness: a liveness probe that fails when
// the database blips will restart every healthy instance at once.
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous()
    .ExcludeFromDescription();

app.MapAuthEndpoints();
app.MapOrgEndpoints();
app.MapApiTokenEndpoints();

app.Run();

/// <summary>Entry point marker so integration tests can boot the real application.</summary>
public partial class Program;
