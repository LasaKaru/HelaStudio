using Microsoft.EntityFrameworkCore;
using Shellwright.Api.Domain;

namespace Shellwright.Api.Data;

/// <summary>The control plane's database context.</summary>
/// <param name="options">Configured options, including the tenant interceptor.</param>
public sealed class ShellwrightDbContext(DbContextOptions<ShellwrightDbContext> options) : DbContext(options)
{
    /// <summary>Organisations.</summary>
    public DbSet<Org> Orgs => Set<Org>();

    /// <summary>Users.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Organisation memberships.</summary>
    public DbSet<OrgMember> OrgMembers => Set<OrgMember>();

    /// <summary>Workspaces.</summary>
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    /// <summary>Apps.</summary>
    public DbSet<AppRecord> Apps => Set<AppRecord>();

    /// <summary>Immutable configuration versions.</summary>
    public DbSet<ConfigVersion> ConfigVersions => Set<ConfigVersion>();

    /// <summary>Uploaded binaries.</summary>
    public DbSet<Asset> Assets => Set<Asset>();

    /// <summary>Audit trail.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <summary>Refresh-token rotation families.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>Long-lived credentials for CI and the command line.</summary>
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    /// <summary>Single-use tokens delivered by email.</summary>
    public DbSet<UserToken> UserTokens => Set<UserToken>();

    /// <summary>Links to external identity providers.</summary>
    public DbSet<OAuthIdentity> OAuthIdentities => Set<OAuthIdentity>();

    /// <summary>Write-only security log.</summary>
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();

    /// <summary>Remembered outcomes of requests that carried an idempotency key.</summary>
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>Builds.</summary>
    public DbSet<Build> Builds => Set<Build>();

    /// <summary>Append-only history of how each build got where it is.</summary>
    public DbSet<BuildTransition> BuildTransitions => Set<BuildTransition>();

    /// <summary>Reusable artifacts, found by the three cache keys.</summary>
    public DbSet<ArtifactCacheEntry> ArtifactCache => Set<ArtifactCacheEntry>();

    /// <summary>Metered builds.</summary>
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Org>(entity =>
        {
            entity.ToTable("orgs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Slug).HasMaxLength(64);
            entity.Property(x => x.Plan).HasConversion<string>().HasMaxLength(16);

            // Partial: a deleted organisation must not hold its slug hostage.
            entity.HasIndex(x => x.Slug).IsUnique().HasFilter("deleted_at IS NULL");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.PasswordHash).HasMaxLength(512);
            entity.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<OrgMember>(entity =>
        {
            entity.ToTable("org_members");
            entity.HasKey(x => new { x.OrgId, x.UserId });
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => x.UserId);
            entity.HasOne(x => x.Org).WithMany(x => x!.Members).HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany(x => x!.Memberships).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.ToTable("workspaces");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Slug).HasMaxLength(64);
            entity.HasIndex(x => new { x.OrgId, x.Slug }).IsUnique();
            entity.HasOne(x => x.Org).WithMany(x => x!.Workspaces).HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppRecord>(entity =>
        {
            entity.ToTable("apps");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.BundleId).HasMaxLength(155);
            entity.HasIndex(x => x.WorkspaceId);
            entity.HasIndex(x => new { x.WorkspaceId, x.BundleId }).IsUnique();
            entity.HasOne(x => x.Workspace).WithMany(x => x!.Apps).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);

            // ⚠️ NoAction, not Cascade or SetNull. Versions are append-only and
            // an app's current pointer is the one mutable link into them;
            // letting the database null it on version deletion would imply
            // version deletion is a thing that happens.
            entity.HasOne(x => x.CurrentConfigVersion)
                .WithMany()
                .HasForeignKey(x => x.CurrentConfigVersionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Build>(entity =>
        {
            entity.ToTable("builds");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.WorkflowId).HasMaxLength(200);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(255);
            entity.Property(x => x.FailureCode).HasMaxLength(100);
            entity.Property(x => x.FailureMessage).HasMaxLength(2000);
            entity.Property(x => x.ArtifactReference).HasMaxLength(100);

            // The listing query: this app's builds, newest first.
            entity.HasIndex(x => new { x.AppId, x.CreatedAt }).IsDescending(false, true);

            // ⚠️ Idempotence as an index, not as a read-then-write. Two
            // identical requests racing each other both find nothing on a read
            // and both start a build — which on a metered fleet is a customer
            // billed twice for one click.
            entity.HasIndex(x => new { x.AppId, x.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName("ix_builds_idempotency");

            // Finding the workflow to cancel.
            entity.HasIndex(x => x.WorkflowId).IsUnique();

            entity.HasOne(x => x.App).WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Org>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Cascade);

            // Restrict, not Cascade: the configuration a build was made from
            // must outlive nothing in particular, but deleting it would leave a
            // build nobody can explain. config_versions has no DELETE grant
            // anyway, so this is belt and braces on something already refused.
            entity.HasOne<ConfigVersion>()
                .WithMany()
                .HasForeignKey(x => x.ConfigVersionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<User>().WithMany().HasForeignKey(x => x.RequestedBy).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<BuildTransition>(entity =>
        {
            entity.ToTable("build_transitions");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.BuildId, x.OccurredAt });
            entity.HasOne(x => x.Build).WithMany().HasForeignKey(x => x.BuildId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArtifactCacheEntry>(entity =>
        {
            entity.ToTable("artifact_cache");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeKey).HasMaxLength(64);
            entity.Property(x => x.AssetKey).HasMaxLength(64);
            entity.Property(x => x.ContentKey).HasMaxLength(64);
            entity.Property(x => x.ArtifactReference).HasMaxLength(100);

            // ⚠️ Unique on the app as well as the keys. Two apps with
            // byte-identical configurations get separate rows: sharing an
            // artifact across tenants would hand one customer's binary to
            // another, and no amount of storage saved is worth that.
            //
            // Type is in the key because a debug-signed artifact must never
            // satisfy a release build.
            entity.HasIndex(x => new { x.AppId, x.Platform, x.Type, x.CodeKey, x.AssetKey, x.ContentKey })
                .IsUnique()
                .HasDatabaseName("ix_artifact_cache_keys");

            // The patch fast path's lookup: everything for this app and
            // platform whose code key matches.
            entity.HasIndex(x => new { x.AppId, x.Platform, x.Type, x.CodeKey })
                .HasDatabaseName("ix_artifact_cache_code_key");

            entity.HasOne(x => x.App).WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UsageRecord>(entity =>
        {
            entity.ToTable("usage_records");
            entity.HasKey(x => x.Id);

            // ⚠️ One row per build, and the index is what makes recording usage
            // idempotent. The activity that writes it is retried on any
            // transient failure, including one that happens after the row was
            // committed, so a read-then-write would double-bill on a network
            // blip.
            entity.HasIndex(x => x.BuildId).IsUnique().HasDatabaseName("ix_usage_records_build");

            // The billing query: one organisation's usage over a period.
            entity.HasIndex(x => new { x.OrgId, x.CreatedAt });

            entity.HasOne<Org>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Cascade);

            // ⚠️ NoAction, deliberately. Usage must outlive the build row it
            // came from: a customer disputing an invoice six months later needs
            // the charge to still exist, and a cascade would quietly erase the
            // evidence along with a cleaned-up build.
            entity.HasOne<Build>().WithMany().HasForeignKey(x => x.BuildId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ConfigVersion>(entity =>
        {
            entity.ToTable("config_versions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Body).HasColumnType("jsonb");
            entity.Property(x => x.CodeKey).HasMaxLength(64);
            entity.Property(x => x.AssetKey).HasMaxLength(64);
            entity.Property(x => x.ContentKey).HasMaxLength(64);
            entity.Property(x => x.Message).HasMaxLength(1000);
            entity.HasIndex(x => new { x.AppId, x.CreatedAt }).IsDescending(false, true);

            // ⚠️ This unique constraint is the whole of "saving an unchanged
            // config is a no-op". Doing it with a SELECT-then-INSERT in
            // application code would be a race that produces duplicate
            // versions under exactly the concurrency a busy studio generates.
            entity.HasIndex(x => new { x.AppId, x.CodeKey, x.AssetKey, x.ContentKey })
                .IsUnique()
                .HasDatabaseName("ix_config_versions_content_address");

            entity.HasOne(x => x.App).WithMany().HasForeignKey(x => x.AppId).OnDelete(DeleteBehavior.Cascade);

            // The author may leave; what they saved does not. SetNull rather
            // than Restrict, so deleting an account is not blocked by the
            // history it created.
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Asset>(entity =>
        {
            entity.ToTable("assets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Sha256).HasMaxLength(64);
            entity.Property(x => x.ContentType).HasMaxLength(64);
            entity.HasIndex(x => new { x.OrgId, x.Sha256 }).IsUnique();
            entity.HasOne<Org>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(64);
            entity.Property(x => x.SubjectType).HasMaxLength(32);
            entity.Property(x => x.SubjectId).HasMaxLength(64);
            entity.Property(x => x.Meta).HasColumnType("jsonb");
            entity.HasIndex(x => new { x.OrgId, x.At }).IsDescending(false, true);

            // ⚠️ Deliberately no foreign key to orgs, and no cascade.
            //
            // The audit trail has to outlive the thing it describes. "Who
            // deleted this organisation, and when" is precisely the record a
            // cascade would remove, and it is the one most likely to be asked
            // for. The org_id is an identifier here, not a reference.
            entity.HasIndex(x => x.ActorId);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64);

            // Presentation is a lookup by hash: the secret has 256 bits of
            // entropy, so an exact index match is both safe and constant-time
            // in the only sense that matters here.
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.FamilyId);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiToken>(entity =>
        {
            entity.ToTable("api_tokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(120);
            entity.Property(x => x.Prefix).HasMaxLength(20);
            entity.Property(x => x.TokenHash).HasMaxLength(64);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => x.OrgId);
            entity.HasOne<Org>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserToken>(entity =>
        {
            entity.ToTable("user_tokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64);
            entity.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.Purpose });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OAuthIdentity>(entity =>
        {
            entity.ToTable("oauth_identities");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Provider).HasMaxLength(32);
            entity.Property(x => x.ProviderUserId).HasMaxLength(128);
            entity.HasIndex(x => new { x.Provider, x.ProviderUserId }).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SecurityEvent>(entity =>
        {
            entity.ToTable("security_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasMaxLength(64);
            entity.Property(x => x.Detail).HasMaxLength(500);
            entity.HasIndex(x => x.At).IsDescending();

            // ⚠️ No foreign key to users. The log has to survive the account it
            // describes, and "this user was deleted" is itself an event worth
            // keeping the surrounding entries for.
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_keys");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(200);
            entity.Property(x => x.Endpoint).HasMaxLength(200);
            entity.Property(x => x.RequestHash).HasMaxLength(64);

            // The natural key. Scoped per user so that one caller's key cannot
            // collide with, or be guessed by, another's.
            entity.HasIndex(x => new { x.UserId, x.Endpoint, x.Key }).IsUnique();
            entity.HasIndex(x => x.ExpiresAt);
        });

        ApplySnakeCaseNames(modelBuilder);
    }

    /// <summary>
    /// Renames every column, key, and index to snake_case.
    /// </summary>
    /// <remarks>
    /// Done as a sweep after the per-entity configuration so that the
    /// configuration above reads as domain modelling rather than as three
    /// hundred lines of <c>HasColumnName</c>. Table names are set explicitly
    /// because plural-vs-singular is a judgement the convention cannot make.
    /// </remarks>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(SnakeCase.Convert(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(SnakeCase.Convert(key.GetName()!));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(SnakeCase.Convert(foreignKey.GetConstraintName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(SnakeCase.Convert(index.GetDatabaseName()!));
            }
        }
    }
}
