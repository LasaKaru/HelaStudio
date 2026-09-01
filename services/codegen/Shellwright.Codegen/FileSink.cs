using System.Collections.Immutable;
using System.Globalization;

namespace Shellwright.Codegen;

/// <summary>Unix permission bits for a generated file.</summary>
public enum FilePermissions
{
    /// <summary>0644 — the mode every generated file has unless it must run.</summary>
    Regular,

    /// <summary>0755 — reserved for <c>gradlew</c> and similar entry points.</summary>
    Executable,
}

/// <summary>One file in a generated project.</summary>
/// <param name="Path">Repository-relative, forward-slashed, never absolute.</param>
/// <param name="Content">The exact bytes to write.</param>
/// <param name="Mode">Permission bits.</param>
public sealed record GeneratedFile(string Path, ImmutableArray<byte> Content, FilePermissions Mode = FilePermissions.Regular)
{
    /// <summary>Size in bytes, for the tree manifest.</summary>
    public int Length => Content.Length;
}

/// <summary>
/// Where a generated project is written.
/// </summary>
/// <remarks>
/// The generator never touches the filesystem itself. In production the sink is
/// a tar stream to R2; in tests it is a dictionary. Writing to disk directly
/// would force a temp directory into every unit test and make the byte-identity
/// assertions — the ones the whole build cache depends on — awkward enough that
/// they would be written less often.
/// </remarks>
public interface IFileSink
{
    /// <summary>Adds one file. Implementations must reject a duplicate path.</summary>
    /// <param name="file">The file to write.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>A task that completes when the file is written.</returns>
    ValueTask WriteAsync(GeneratedFile file, CancellationToken cancellationToken = default);
}

/// <summary>An <see cref="IFileSink"/> that keeps everything in memory.</summary>
/// <remarks>
/// The sink used by every test. Its <see cref="Files"/> are ordered by path so
/// two generations can be compared directly, without the caller having to
/// remember to sort first — the kind of detail that makes a determinism test
/// pass for the wrong reason.
/// </remarks>
public sealed class InMemoryFileSink : IFileSink
{
    private readonly SortedDictionary<string, GeneratedFile> files =
        new(StringComparer.Ordinal);

    /// <summary>Every file written so far, ordered by path.</summary>
    public IReadOnlyList<GeneratedFile> Files => [.. files.Values];

    /// <summary>Looks one file up by its relative path.</summary>
    /// <param name="path">The relative path.</param>
    /// <returns>The file, or null if it was not generated.</returns>
    public GeneratedFile? Find(string path) => files.GetValueOrDefault(path);

    /// <summary>Reads one file's content as UTF-8 text.</summary>
    /// <param name="path">The relative path.</param>
    /// <returns>The decoded content.</returns>
    /// <exception cref="KeyNotFoundException">The file was not generated.</exception>
    public string Text(string path)
    {
        var file = Find(path)
            ?? throw new KeyNotFoundException($"No generated file at '{path}'.");
        return System.Text.Encoding.UTF8.GetString(file.Content.AsSpan());
    }

    /// <inheritdoc/>
    public ValueTask WriteAsync(GeneratedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();

        // A duplicate path means two template rules claim the same output, and
        // whichever ran last would silently win. Failing here turns a
        // config-dependent mystery into a generation-time error.
        if (!files.TryAdd(file.Path, file))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{file.Path}' was generated twice. Two rules claim the same output path."));
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>An <see cref="IFileSink"/> that writes into a directory on disk.</summary>
/// <remarks>
/// Used by the nightly real-build job and by anyone inspecting output by hand.
/// Not used by unit tests.
/// </remarks>
public sealed class DirectoryFileSink : IFileSink
{
    private readonly string root;

    /// <summary>Creates a sink rooted at <paramref name="root"/>.</summary>
    /// <param name="root">An existing directory to write into.</param>
    public DirectoryFileSink(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = System.IO.Path.GetFullPath(root);
    }

    /// <inheritdoc/>
    public async ValueTask WriteAsync(GeneratedFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, file.Path));

        // ⚠️ A relative path is user-influenced by way of locale codes and
        // plugin ids. Refusing anything that escapes the root turns a possible
        // arbitrary-write into a generation error.
        if (!target.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"'{file.Path}' escapes the output directory."));
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, file.Content.AsSpan().ToArray(), cancellationToken)
            .ConfigureAwait(false);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                target,
                file.Mode == FilePermissions.Executable
                    ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite
                        | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }
    }
}
