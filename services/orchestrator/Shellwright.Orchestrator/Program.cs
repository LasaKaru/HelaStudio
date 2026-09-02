using Microsoft.Extensions.Hosting;
using Shellwright.Orchestrator.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddShellwrightOrchestrator(builder.Configuration);

await builder.Build().RunAsync();

/// <summary>Entry point marker so tests can reference the worker host.</summary>
public partial class Program;
