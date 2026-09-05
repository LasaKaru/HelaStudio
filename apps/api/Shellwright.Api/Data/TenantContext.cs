namespace Shellwright.Api.Data;

/// <summary>
/// Carries the identity that database row-level security is evaluated against
/// for the current unit of work.
/// </summary>
/// <remarks>
/// ⚠️ This is deliberately a mutable scoped object rather than something read
/// out of <c>HttpContext</c> on demand. Background work, migrations, and tests
/// all need to state whose rows they are allowed to see, and several of them
/// have no HTTP request at all. Making the dependency explicit means the
/// interceptor has exactly one place to look.
/// </remarks>
public sealed class TenantContext
{
    /// <summary>
    /// The user whose access governs this connection, or null for an
    /// unauthenticated request.
    /// </summary>
    /// <remarks>
    /// Null is not "see everything" — it is "see nothing". Every policy
    /// compares against this value, and a comparison against NULL is never
    /// true, so an unset identity fails closed. That property is what
    /// <c>RowLevelSecurityTests.Unset_identity_sees_nothing</c> exists to
    /// pin down.
    /// </remarks>
    public Guid? UserId { get; set; }

    /// <summary>Sets the identity and returns a scope that restores the previous value.</summary>
    /// <param name="userId">The user to act as, or null to act as nobody.</param>
    /// <returns>A disposable that restores the previous identity.</returns>
    public IDisposable Impersonate(Guid? userId)
    {
        var previous = UserId;
        UserId = userId;
        return new Restore(this, previous);
    }

    private sealed class Restore(TenantContext context, Guid? previous) : IDisposable
    {
        public void Dispose() => context.UserId = previous;
    }
}
