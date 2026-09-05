namespace Shellwright.Api.Domain;

/// <summary>
/// Membership roles, ordered from least to most privileged.
/// </summary>
/// <remarks>
/// The numeric order is load-bearing: every permission check in the API is
/// expressed as "at least this role", so adding a role between two existing
/// ones means renumbering, not just appending. The values are persisted as
/// their string names, never as integers, so renumbering stays a code change.
/// </remarks>
public enum OrgRole
{
    /// <summary>Can read apps and configurations, and nothing else.</summary>
    Viewer = 0,

    /// <summary>Can save configuration versions and trigger builds.</summary>
    Developer = 1,

    /// <summary>Can manage members, signing credentials, and store submissions.</summary>
    Admin = 2,

    /// <summary>Can manage billing and delete the organisation.</summary>
    Owner = 3,
}

/// <summary>Billing plan attached to an organisation.</summary>
public enum OrgPlan
{
    /// <summary>No payment method; build minutes and app count are capped.</summary>
    Free = 0,

    /// <summary>Paid plan. Introduced properly in Sprint 17.</summary>
    Pro = 1,
}
