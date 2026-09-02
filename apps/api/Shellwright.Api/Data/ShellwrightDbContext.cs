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
