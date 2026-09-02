using FluentAssertions;
using Shellwright.Api.Authorization;
using Shellwright.Api.Domain;
using Xunit;

namespace Shellwright.Api.Tests;

/// <summary>
/// The permission matrix from <c>sprints/SPRINT-06.md</c> §T-06.3, written out
/// so that changing it is deliberate.
/// </summary>
/// <remarks>
/// ⚠️ Reproducing the table rather than deriving it from
/// <see cref="Permissions"/> is the whole point. A test that read the same
/// constants the code reads would pass no matter what those constants said; a
/// test that spells out the intended answer fails when somebody changes one,
/// which is exactly when a person should look.
/// </remarks>
public sealed class PermissionMatrixTests
{
    /// <summary>Read an app and its configuration: everybody.</summary>
    [Theory]
    [InlineData(OrgRole.Viewer, true)]
    [InlineData(OrgRole.Developer, true)]
    [InlineData(OrgRole.Admin, true)]
    [InlineData(OrgRole.Owner, true)]
    public void Read_app(OrgRole role, bool allowed) =>
        (role >= Permissions.ReadApp).Should().Be(allowed);

    /// <summary>Save a configuration version: not a viewer.</summary>
    [Theory]
    [InlineData(OrgRole.Viewer, false)]
    [InlineData(OrgRole.Developer, true)]
    [InlineData(OrgRole.Admin, true)]
    [InlineData(OrgRole.Owner, true)]
    public void Save_config_version(OrgRole role, bool allowed) =>
        (role >= Permissions.SaveConfigVersion).Should().Be(allowed);

    /// <summary>Trigger a build: not a viewer.</summary>
    [Theory]
    [InlineData(OrgRole.Viewer, false)]
    [InlineData(OrgRole.Developer, true)]
    [InlineData(OrgRole.Admin, true)]
    [InlineData(OrgRole.Owner, true)]
    public void Trigger_build(OrgRole role, bool allowed) =>
        (role >= Permissions.TriggerBuild).Should().Be(allowed);

    /// <summary>Manage signing credentials: admin and above.</summary>
    [Theory]
    [InlineData(OrgRole.Viewer, false)]
    [InlineData(OrgRole.Developer, false)]
    [InlineData(OrgRole.Admin, true)]
    [InlineData(OrgRole.Owner, true)]
    public void Manage_signing_credentials(OrgRole role, bool allowed) =>
        (role >= Permissions.ManageSigningCredentials).Should().Be(allowed);

    /// <summary>Submit to a store: admin and above.</summary>
    [Theory]
    [InlineData(OrgRole.Viewer, false)]
    [InlineData(OrgRole.Developer, false)]
    [InlineData(OrgRole.Admin, true)]
    [InlineData(OrgRole.Owner, true)]
    public void Submit_to_store(OrgRole role, bool allowed) =>
        (role >= Permissions.SubmitToStore).Should().Be(allowed);

    /// <summary>Manage members: admin and above.</summary>
    [Theory]
    [InlineData(OrgRole.Viewer, false)]
    [InlineData(OrgRole.Developer, false)]
    [InlineData(OrgRole.Admin, true)]
    [InlineData(OrgRole.Owner, true)]
    public void Manage_members(OrgRole role, bool allowed) =>
        (role >= Permissions.ManageMembers).Should().Be(allowed);

    /// <summary>Billing: owner only.</summary>
    [Theory]
    [InlineData(OrgRole.Viewer, false)]
    [InlineData(OrgRole.Developer, false)]
    [InlineData(OrgRole.Admin, false)]
    [InlineData(OrgRole.Owner, true)]
    public void Manage_billing(OrgRole role, bool allowed) =>
        (role >= Permissions.ManageBilling).Should().Be(allowed);

    /// <summary>Delete the organisation: owner only.</summary>
    [Theory]
    [InlineData(OrgRole.Viewer, false)]
    [InlineData(OrgRole.Developer, false)]
    [InlineData(OrgRole.Admin, false)]
    [InlineData(OrgRole.Owner, true)]
    public void Delete_org(OrgRole role, bool allowed) =>
        (role >= Permissions.DeleteOrg).Should().Be(allowed);

    /// <summary>
    /// The ordering the whole matrix rests on.
    /// </summary>
    /// <remarks>
    /// Every check above is a <c>&gt;=</c> against an enum value, so the
    /// numeric order is load-bearing. Inserting a role in the middle without
    /// renumbering would silently reshuffle who can do what.
    /// </remarks>
    [Fact]
    public void Roles_are_ordered_from_least_to_most_privileged()
    {
        var ordered = Enum.GetValues<OrgRole>().OrderBy(x => (int)x).ToArray();

        ordered.Should().Equal(OrgRole.Viewer, OrgRole.Developer, OrgRole.Admin, OrgRole.Owner);
    }
}
