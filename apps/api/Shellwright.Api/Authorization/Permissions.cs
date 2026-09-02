using System.Diagnostics.CodeAnalysis;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Authorization;

/// <summary>
/// The minimum role each action requires.
/// </summary>
/// <remarks>
/// <para>
/// One table, referenced by every endpoint, rather than a role name written
/// out at each call site. The difference shows up the first time a permission
/// moves: with a table it is one edit and the tests fail loudly if the matrix
/// in <c>sprints/SPRINT-06.md</c> disagrees; without one it is a search for
/// every place somebody typed <c>Admin</c>.
/// </para>
/// <para>
/// ⚠️ Several of these guard capabilities that do not exist yet — builds arrive
/// in Sprint 07, signing credentials in Sprint 14, billing in Sprint 17. They
/// are here because deciding who may hold a customer's signing key is a
/// decision worth making now, calmly, rather than in the sprint where the
/// feature is being built.
/// </para>
/// </remarks>
[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "The clash is with System.Security.Permissions, a .NET Framework namespace this project "
        + "cannot reference and never will. Renaming to something like PermissionMatrix would make every call "
        + "site read worse in exchange for avoiding an ambiguity that cannot arise.")]
public static class Permissions
{
    /// <summary>Read an app and its configuration.</summary>
    public const OrgRole ReadApp = OrgRole.Viewer;

    /// <summary>Save a new configuration version.</summary>
    public const OrgRole SaveConfigVersion = OrgRole.Developer;

    /// <summary>Start a build. Sprint 07.</summary>
    public const OrgRole TriggerBuild = OrgRole.Developer;

    /// <summary>Create an app.</summary>
    public const OrgRole CreateApp = OrgRole.Developer;

    /// <summary>Create a workspace.</summary>
    public const OrgRole CreateWorkspace = OrgRole.Admin;

    /// <summary>
    /// Mint an API token.
    /// </summary>
    /// <remarks>
    /// Developer rather than Admin, and safe only because a token's role is
    /// capped by its creator's own membership: a developer can mint a developer
    /// token and nothing more. Requiring an admin instead would mean every CI
    /// pipeline waits on one, which is how shared tokens get created.
    /// </remarks>
    public const OrgRole CreateApiToken = OrgRole.Developer;

    /// <summary>Revoke somebody else's API token.</summary>
    public const OrgRole RevokeOthersApiToken = OrgRole.Admin;

    /// <summary>Hold and rotate customer signing material. Sprint 14.</summary>
    public const OrgRole ManageSigningCredentials = OrgRole.Admin;

    /// <summary>Submit a build to a store. Sprint 15.</summary>
    public const OrgRole SubmitToStore = OrgRole.Admin;

    /// <summary>Add, remove, and re-role members.</summary>
    public const OrgRole ManageMembers = OrgRole.Admin;

    /// <summary>Change the plan or the payment method. Sprint 17.</summary>
    public const OrgRole ManageBilling = OrgRole.Owner;

    /// <summary>Delete the organisation.</summary>
    public const OrgRole DeleteOrg = OrgRole.Owner;
}
