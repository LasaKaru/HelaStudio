using System.Diagnostics;

namespace Shellwright.Orchestrator.Sandbox;

/// <summary>
/// Runs one process, streaming its output line by line and killing it on
/// cancellation.
/// </summary>
/// <remarks>
/// Shared by both sandboxes: the Docker one runs <c>docker</c>, the local one
/// runs the build tool directly, and the process handling — argument arrays,
/// line framing, progress ticks, killing the tree — is identical and worth
/// having in one place rather than two.
/// </remarks>
public static class ProcessSandboxRunner
{
    /// <summary>Runs a process to completion.</summary>
    /// <param name="executable">The program.</param>
    /// <param name="arguments">Arguments, one element per argument.</param>
    /// <param name="workingDirectory">Where to run it.</param>
    /// <param name="environment">Extra environment variables.</param>
    /// <param name="onLine">Receives each line as it is produced.</param>
    /// <param name="onProgress">Called periodically so the caller can heartbeat.</param>
    /// <param name="progressInterval">How often to call <paramref name="onProgress"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How it ended.</returns>
    public static async Task<SandboxResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        LogLineHandler onLine,
        Action? onProgress,
        TimeSpan progressInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(onLine);

        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // ⚠️ ArgumentList, not Arguments. The string form is parsed, and the
        // values here include an app name the customer chose.
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                start.Environment[key] = value;
            }
        }

        var stopwatch = Stopwatch.StartNew();

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        using var progress = onProgress is null
            ? null
            : new Timer(_ => onProgress(), null, progressInterval, progressInterval);

        var reading = Task.WhenAll(
            PumpAsync(process.StandardOutput, isError: false, onLine, cancellationToken),
            PumpAsync(process.StandardError, isError: true, onLine, cancellationToken));

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // ⚠️ Kill the tree, not the process. Gradle spawns a JVM and often
            // a Kotlin compile daemon; killing only the launcher leaves them
            // holding the workspace and the memory, which is precisely the
            // "cancellation freed nothing" failure this has to avoid.
            Terminate(process);
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }

        // Output written just before exit is still in flight when the process
        // ends; waiting for the pumps is what stops the last lines — usually
        // the error — being the ones that go missing.
        await reading;

        return new SandboxResult(process.ExitCode, stopwatch.Elapsed);
    }

    /// <summary>Kills a process and everything it started, ignoring races.</summary>
    /// <param name="process">The process.</param>
    public static void Terminate(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // It exited between the check and the kill. That is the outcome
            // asked for.
        }
        catch (NotSupportedException)
        {
            // No process tree on this platform; the process itself is gone.
        }
    }

    private static async Task PumpAsync(
        StreamReader reader,
        bool isError,
        LogLineHandler onLine,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            await onLine(line, isError, cancellationToken);
        }
    }
}
