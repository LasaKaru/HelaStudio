using System.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace Shellwright.Api.Observability;

/// <summary>Observability settings.</summary>
public sealed class TelemetryOptions
{
    /// <summary>Configuration section these settings bind to.</summary>
    public const string SectionName = "Telemetry";

    /// <summary>Service name reported in traces and metrics.</summary>
    public string ServiceName { get; set; } = "shellwright-api";

    /// <summary>
    /// OTLP collector endpoint. Empty disables export.
    /// </summary>
    /// <remarks>
    /// Empty is the sensible default rather than a localhost guess: an exporter
    /// pointed at a collector that is not there retries on a background thread
    /// and fills the log with connection failures, which is worse than no
    /// telemetry.
    /// </remarks>
    public string OtlpEndpoint { get; set; } = string.Empty;
}

/// <summary>Logging, tracing, and metrics.</summary>
public static class ObservabilityExtensions
{
    /// <summary>The activity source spans raised by this application belong to.</summary>
    public static ActivitySource ActivitySource { get; } = new("Shellwright.Api");

    /// <summary>Replaces the default logger with structured JSON.</summary>
    /// <param name="builder">The host builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// ⚠️ JSON in every environment, including a developer's terminal. Two
    /// formats means two sets of parsing rules and one of them is only ever
    /// exercised in production, which is where discovering it is wrong costs
    /// the most.
    /// </remarks>
    public static IHostApplicationBuilder AddShellwrightLogging(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", "shellwright-api")
            .WriteTo.Console(new Serilog.Formatting.Compact.CompactJsonFormatter())
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(logger, dispose: true);

        return builder;
    }

    /// <summary>Adds traces and metrics.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddShellwrightTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetryOptions.SectionName));

        var telemetry = configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>()
            ?? new TelemetryOptions();

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(telemetry.ServiceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(ActivitySource.Name)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Health probes run every few seconds forever and say
                        // nothing about how the API is behaving.
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal);
                    })
                    .AddHttpClientInstrumentation()
                    .AddNpgsql();

                if (!string.IsNullOrWhiteSpace(telemetry.OtlpEndpoint))
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(telemetry.OtlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                // The four golden signals come from these two instrumentations:
                // rate, errors, and duration from the ASP.NET Core meter,
                // saturation from the runtime's thread-pool and GC counters.
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(telemetry.OtlpEndpoint))
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(telemetry.OtlpEndpoint));
                }
            });

        return services;
    }
}
