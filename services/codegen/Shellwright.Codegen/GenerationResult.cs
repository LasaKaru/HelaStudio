using System.Collections.Immutable;

namespace Shellwright.Codegen;

/// <summary>One entry in a generated project's tree manifest.</summary>
/// <param name="Path">Repository-relative, forward-slashed.</param>
/// <param name="Mode">Permission bits.</param>
/// <param name="Length">Size in bytes.</param>
/// <param name="Hash">BLAKE3 of the content, lowercase hex.</param>
public sealed record TreeEntry(string Path, FilePermissions Mode, int Length, string Hash);

/// <summary>What a generation run produced.</summary>
/// <param name="Files">Every file, ordered by path.</param>
/// <param name="Hashes">The three cache keys for the config that produced it.</param>
/// <param name="TreeHash">BLAKE3 over the whole tree manifest.</param>
public sealed record GenerationResult(
    ImmutableArray<TreeEntry> Files,
    ConfigSchema.ConfigHashes Hashes,
    string TreeHash);

/// <summary>Turns a resolved configuration into a buildable project.</summary>
public interface IProjectGenerator
{
    /// <summary>Generates a project into <paramref name="sink"/>.</summary>
    /// <param name="resolved">A configuration with schema defaults applied.</param>
    /// <param name="toolchain">Tool versions the output depends on.</param>
    /// <param name="sink">Where files are written.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The tree manifest and cache keys.</returns>
    Task<GenerationResult> GenerateAsync(
        System.Text.Json.Nodes.JsonObject resolved,
        ToolchainDescriptor toolchain,
        IFileSink sink,
        CancellationToken cancellationToken = default);
}
