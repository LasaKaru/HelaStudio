using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Shellwright.Orchestrator.Logs;

/// <summary>How important a log line is.</summary>
public enum LogSeverity
{
    /// <summary>Ordinary progress.</summary>
    Info = 0,

    /// <summary>Something worth reading, that did not stop the build.</summary>
    Warning = 1,

    /// <summary>Something that did.</summary>
    Error = 2,
}

/// <summary>One line of build output, ready to store.</summary>
/// <param name="Text">The line, redacted.</param>
/// <param name="Severity">How important it is.</param>
/// <param name="Redacted">Whether anything was removed.</param>
public sealed record LogLine(string Text, LogSeverity Severity, bool Redacted);

/// <summary>
/// Removes credentials from build output before it is stored.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Before it is written, never before it is displayed. A redaction applied
/// at render time leaves the secret in the archive, in the Redis stream, and in
/// whatever else read from either — and the archive is the copy that lives for
/// years. Redacting on the way in means the secret exists in one process's
/// memory for the length of one line.
/// </para>
/// <para>
/// The patterns come from what build tools actually print. Gradle echoes the
/// full command line on failure, including any <c>-P</c> property; apksigner
/// and keytool name keystore paths and occasionally more; a plugin that logs
/// its own configuration will happily log a token. This is a filter over known
/// shapes rather than a proof, so it is paired with a corpus of real tool
/// output in <c>tests/fixtures/log-redaction/</c> that grows every time
/// something new leaks.
/// </para>
/// <para>
/// ⚠️ It fails open by design — an unrecognised secret passes through. That is
/// worth saying out loud rather than implying otherwise: the defence that
/// matters is not printing secrets, and this is the net beneath it.
/// </para>
/// </remarks>
public static partial class LogRedaction
{
    /// <summary>What replaces a redacted value.</summary>
    public const string Placeholder = "[redacted]";

    private static readonly ImmutableArray<Func<string, string>> Filters =
    [
        line => KeyValueSecret().Replace(line, m => m.Groups["key"].Value + m.Groups["sep"].Value + Placeholder),
        line => GradleProperty().Replace(line, m => m.Groups["prefix"].Value + Placeholder),
        line => Bearer().Replace(line, "Bearer " + Placeholder),
        line => BasicAuth().Replace(line, "Basic " + Placeholder),
        line => UrlCredentials().Replace(line, m => m.Groups["scheme"].Value + Placeholder + "@"),
        line => PrivateKeyBlock().Replace(line, Placeholder),
        line => AwsKey().Replace(line, Placeholder),
        line => GitHubToken().Replace(line, Placeholder),
        line => GoogleKey().Replace(line, Placeholder),
        line => JsonWebToken().Replace(line, Placeholder),
    ];

    /// <summary>Redacts and classifies one line.</summary>
    /// <param name="line">The raw line, as the tool printed it.</param>
    /// <param name="isError">Whether it arrived on standard error.</param>
    /// <returns>The line as it should be stored.</returns>
    public static LogLine Process(string line, bool isError)
    {
        ArgumentNullException.ThrowIfNull(line);

        var redacted = Redact(line);

        return new LogLine(
            redacted,
            Classify(line, isError),
            !string.Equals(redacted, line, StringComparison.Ordinal));
    }

    /// <summary>Removes every credential shape this filter knows.</summary>
    /// <param name="line">The raw line.</param>
    /// <returns>The line with credentials replaced.</returns>
    public static string Redact(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        foreach (var filter in Filters)
        {
            line = filter(line);
        }

        return line;
    }

    /// <summary>
    /// Decides how important a line is.
    /// </summary>
    /// <param name="line">The raw line.</param>
    /// <param name="isError">Whether it arrived on standard error.</param>
    /// <returns>The severity.</returns>
    /// <remarks>
    /// ⚠️ The stream a line arrived on is a weak signal and is deliberately not
    /// the decision. Gradle writes its progress to standard error and its
    /// compilation errors to standard output, so trusting the stream would
    /// classify a successful build as a wall of errors and hide the one line
    /// that matters. Gradle output is mostly noise; surfacing the three lines
    /// somebody needs is a real usability win, and it has to be done by
    /// looking at the text.
    /// </remarks>
    public static LogSeverity Classify(string line, bool isError)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (ErrorLine().IsMatch(line))
        {
            return LogSeverity.Error;
        }

        if (WarningLine().IsMatch(line))
        {
            return LogSeverity.Warning;
        }

        return isError && line.Length > 0 && !ProgressNoise().IsMatch(line)
            ? LogSeverity.Warning
            : LogSeverity.Info;
    }

    // A key whose name says it is a secret, followed by a value.
    [GeneratedRegex(
        @"(?<key>(?i:pass(word|phrase)?|secret|token|credential|api[_-]?key|auth))(?<sep>\s*[:=]\s*)\S+",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex KeyValueSecret();

    // Gradle echoes its whole command line on failure, including -P properties.
    [GeneratedRegex(
        @"(?<prefix>-P\s*[A-Za-z0-9._]*(?i:password|secret|token|key)[A-Za-z0-9._]*\s*=\s*)\S+",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex GradleProperty();

    [GeneratedRegex(@"(?i:Bearer)\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"(?i:Basic)\s+[A-Za-z0-9+/=]{8,}", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex BasicAuth();

    // https://user:password@host — the password half is what matters.
    [GeneratedRegex(
        @"(?<scheme>[a-zA-Z][a-zA-Z0-9+.-]*://)[^\s:/@]+:[^\s:/@]+@",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 200)]
    private static partial Regex UrlCredentials();

    [GeneratedRegex(@"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex PrivateKeyBlock();

    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex AwsKey();

    [GeneratedRegex(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex GitHubToken();

    // ⚠️ {35,} rather than {35}. A real key is AIza plus exactly 35 characters,
    // and a length-exact pattern misses anything a character longer — which is
    // how this was found: a fixture written from memory had 36 and sailed
    // through. Over-redacting a near-miss is the harmless direction.
    [GeneratedRegex(@"\bAIza[0-9A-Za-z_-]{35,}\b", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex GoogleKey();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex JsonWebToken();

    [GeneratedRegex(@"(?i:^\s*(e:|error:|FAILURE:|\* What went wrong:|Execution failed for task)|\bexception\b|\bunresolved reference\b)", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex ErrorLine();

    [GeneratedRegex(@"(?i:^\s*(w:|warning:)|\bdeprecat(ed|ion)\b)", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex WarningLine();

    // Gradle's progress bar and download chatter arrive on standard error and
    // mean nothing.
    [GeneratedRegex(@"(?i:^\s*(Download|Starting a Gradle Daemon|<[=\-]+>|\d+% |Welcome to Gradle))", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex ProgressNoise();
}
