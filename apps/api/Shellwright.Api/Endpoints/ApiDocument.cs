using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Shellwright.Api.Endpoints;

/// <summary>The published OpenAPI description.</summary>
/// <remarks>
/// ⚠️ The document is generated from the route table, not hand-written, so it
/// cannot describe an endpoint that does not exist or miss one that does. The
/// TypeScript client is generated from the document in turn, and CI fails when
/// the committed client no longer matches — which is what stops the studio and
/// the API drifting apart between releases.
/// </remarks>
public static class ApiDocument
{
    /// <summary>Command-line switch that writes the document and exits.</summary>
    public const string ExportSwitch = "--export-openapi";

    /// <summary>Fills in the parts of the document that are not derivable from routes.</summary>
    /// <param name="options">Options for the document.</param>
    public static void Configure(OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Shellwright control plane",
                Version = "v1",
                Description =
                    "Organisations, apps, and immutable configuration versions.\n\n"
                    + "Errors are RFC 9457 problem documents. The `type` URI and the `code` extension "
                    + "are stable; the `title` and `detail` are for people and may be reworded.",
            };

            return Task.CompletedTask;
        });
    }

    /// <summary>Whether the process was asked to write the document rather than serve.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="destination">Where to write it.</param>
    /// <returns>True when the document should be written.</returns>
    public static bool ShouldExport(string[] args, out string destination)
    {
        ArgumentNullException.ThrowIfNull(args);

        destination = string.Empty;

        var index = Array.IndexOf(args, ExportSwitch);

        if (index < 0 || index + 1 >= args.Length)
        {
            return false;
        }

        destination = args[index + 1];
        return true;
    }

    /// <summary>Writes the document to a file.</summary>
    /// <param name="app">The built application.</param>
    /// <param name="destination">Path to write to.</param>
    /// <returns>A task that completes once the file is written.</returns>
    public static async Task WriteAsync(WebApplication app, string destination)
    {
        ArgumentNullException.ThrowIfNull(app);

        // ⚠️ The host has to start before the document has any paths in it.
        //
        // Minimal-API routes live on the WebApplication until the pipeline is
        // built, and only then are they registered with the EndpointDataSource
        // the generator reads. Asking for the document without starting
        // produces a valid, complete-looking file with an empty `paths` object
        // — which is exactly the kind of output a CI step would happily commit.
        //
        // Port 0 so this never collides with anything already listening.
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();

        try
        {
            await WriteDocumentAsync(app, destination);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static async Task WriteDocumentAsync(WebApplication app, string destination)
    {
        // Keyed by document name: one application can publish several, and
        // asking for the unkeyed service silently gets you whichever was
        // registered last.
        var provider = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        var document = await provider.GetOpenApiDocumentAsync();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);

        // Indented and with a trailing newline, because this file is committed
        // and read in diffs. A single-line document turns every change into one
        // unreviewable line.
        await using var stream = File.Create(destination);
        await using var writer = new StreamWriter(stream);

        document.SerializeAsV3(new OpenApiJsonWriter(writer, new OpenApiJsonWriterSettings { Terse = false }));
        await writer.WriteLineAsync();
    }
}
