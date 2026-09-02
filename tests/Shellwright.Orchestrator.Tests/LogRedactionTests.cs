using FluentAssertions;
using Shellwright.Orchestrator.Logs;
using Shellwright.Orchestrator.Tests.Infrastructure;
using Xunit;

namespace Shellwright.Orchestrator.Tests;

/// <summary>
/// TC-S07-SEC-003 — credentials do not reach the archive.
/// </summary>
/// <remarks>
/// ⚠️ Driven from a corpus of real tool output rather than from invented
/// strings, because a filter written against imagined input matches imagined
/// input. Gradle echoing its own command line on failure is the case that
/// actually leaks a keystore password, and it looks nothing like what you would
/// guess.
/// </remarks>
public sealed class LogRedactionTests
{
    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();

        foreach (var entry in RedactionCorpus.Cases)
        {
            data.Add(entry.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void The_corpus_is_redacted(string name)
    {
        var entry = RedactionCorpus.Cases.Single(x => x.Name == name);
        var redacted = LogRedaction.Redact(entry.Line);

        foreach (var secret in entry.MustNotContain)
        {
            redacted.Should().NotContain(secret, "'{0}' must not reach the archive", name);
        }

        foreach (var kept in entry.MustContain ?? [])
        {
            redacted.Should().Contain(kept, "'{0}' has to stay debuggable", name);
        }
    }

    /// <summary>Redaction reports that it did something, so the UI can say so.</summary>
    [Fact]
    public void A_redacted_line_is_flagged()
    {
        var line = LogRedaction.Process("storePassword=hunter2", isError: false);

        line.Redacted.Should().BeTrue();
        line.Text.Should().Contain(LogRedaction.Placeholder);
    }

    /// <summary>An ordinary line is not flagged.</summary>
    [Fact]
    public void An_ordinary_line_is_not_flagged()
    {
        var line = LogRedaction.Process("> Task :app:compileReleaseKotlin", isError: false);

        line.Redacted.Should().BeFalse();
        line.Text.Should().Be("> Task :app:compileReleaseKotlin");
    }

    /// <summary>
    /// Classification reads the text, not the stream it arrived on.
    /// </summary>
    /// <remarks>
    /// ⚠️ Gradle writes progress to standard error and compilation errors to
    /// standard output. Trusting the stream would mark a clean build as a wall
    /// of errors and bury the one line that matters.
    /// </remarks>
    [Theory]
    [InlineData("e: file:///app/Main.kt:12:5 Unresolved reference: foo", false, LogSeverity.Error)]
    [InlineData("FAILURE: Build failed with an exception.", false, LogSeverity.Error)]
    [InlineData("* What went wrong:", false, LogSeverity.Error)]
    [InlineData("Execution failed for task ':app:lintRelease'.", false, LogSeverity.Error)]
    [InlineData("w: file:///app/Main.kt:3:1 Parameter 'x' is never used", false, LogSeverity.Warning)]
    [InlineData("warning: [deprecation] doThing() is deprecated", false, LogSeverity.Warning)]
    [InlineData("Download https://repo.maven.apache.org/thing.jar", true, LogSeverity.Info)]
    [InlineData("Welcome to Gradle 8.14.3!", true, LogSeverity.Info)]
    [InlineData("> Task :app:compileReleaseKotlin", false, LogSeverity.Info)]
    public void Lines_are_classified_by_content(string line, bool isError, LogSeverity expected) =>
        LogRedaction.Classify(line, isError).Should().Be(expected);

    /// <summary>
    /// The filter is bounded, so a hostile line cannot hang the pipeline.
    /// </summary>
    /// <remarks>
    /// ⚠️ Every pattern carries a match timeout. Build output is attacker-influenced
    /// — a dependency can print whatever it likes — and a redaction filter that
    /// backtracks is a denial of service on the log pipeline, which is on the
    /// path of every build.
    /// </remarks>
    [Fact]
    public void A_pathological_line_does_not_hang_the_filter()
    {
        var hostile = "password=" + new string('a', 50_000) + "!";

        var start = DateTimeOffset.UtcNow;
        var redacted = LogRedaction.Redact(hostile);
        var elapsed = DateTimeOffset.UtcNow - start;

        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        redacted.Should().Contain(LogRedaction.Placeholder);
    }

    /// <summary>An empty line survives as an empty line.</summary>
    [Fact]
    public void An_empty_line_is_untouched() =>
        LogRedaction.Redact(string.Empty).Should().BeEmpty();

    /// <summary>
    /// The corpus contains cases that must survive as well as cases that must not.
    /// </summary>
    /// <remarks>
    /// A filter that redacted everything would pass every positive case and be
    /// useless. This asserts the corpus itself keeps both halves.
    /// </remarks>
    [Fact]
    public void The_corpus_covers_both_directions()
    {
        RedactionCorpus.Cases.Should().Contain(x => x.MustNotContain.Count > 0);
        RedactionCorpus.Cases.Count(x => x.MustContain != null && x.MustContain.Count > 0)
            .Should().BeGreaterThan(0);
    }
}
