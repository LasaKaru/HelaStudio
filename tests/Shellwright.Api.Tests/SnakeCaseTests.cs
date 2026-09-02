using FluentAssertions;
using Shellwright.Api.Data;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>
/// The naming rule that every migration is written against.
/// </summary>
/// <remarks>
/// These look like trivia and are not: the conversion runs once, at model
/// build, and its output is frozen into migration files. Changing it later
/// renames columns underneath a live database, so the cases below are a
/// contract rather than documentation.
/// </remarks>
public sealed class SnakeCaseTests
{
    [Theory]
    [InlineData("Id", "id")]
    [InlineData("OrgId", "org_id")]
    [InlineData("CurrentConfigVersionId", "current_config_version_id")]
    [InlineData("Sha256", "sha256")]
    [InlineData("EmailVerifiedAt", "email_verified_at")]
    [InlineData("pk_orgs", "pk_orgs")]
    [InlineData("IX_apps_WorkspaceId", "ix_apps_workspace_id")]
    [InlineData("", "")]
    public void Converts(string input, string expected) =>
        SnakeCase.Convert(input).Should().Be(expected);

    /// <summary>An acronym run ends where the next word starts, not at its last letter.</summary>
    [Theory]
    [InlineData("HTTPResponse", "http_response")]
    [InlineData("ID", "id")]
    [InlineData("OTAChannel", "ota_channel")]
    public void Handles_acronyms(string input, string expected) =>
        SnakeCase.Convert(input).Should().Be(expected);
}
