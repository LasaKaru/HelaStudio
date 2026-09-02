using System;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shellwright.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CoreTenantModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    meta = table.Column<JsonObject>(type: "jsonb", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "orgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    plan = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orgs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_assets_orgs_org_id",
                        column: x => x.org_id,
                        principalTable: "orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workspaces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_workspaces", x => x.id);
                    table.ForeignKey(
                        name: "fk_workspaces_orgs_org_id",
                        column: x => x.org_id,
                        principalTable: "orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "org_members",
                columns: table => new
                {
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_members", x => new { x.org_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_org_members_orgs_org_id",
                        column: x => x.org_id,
                        principalTable: "orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_org_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    bundle_id = table.Column<string>(type: "character varying(155)", maxLength: 155, nullable: false),
                    current_config_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_apps", x => x.id);
                    table.ForeignKey(
                        name: "fk_apps_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "config_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<JsonObject>(type: "jsonb", nullable: false),
                    code_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    asset_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_config_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_config_versions_apps_app_id",
                        column: x => x.app_id,
                        principalTable: "apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_config_versions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_apps_current_config_version_id",
                table: "apps",
                column: "current_config_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_apps_workspace_id",
                table: "apps",
                column: "workspace_id");

            migrationBuilder.CreateIndex(
                name: "ix_apps_workspace_id_bundle_id",
                table: "apps",
                columns: new[] { "workspace_id", "bundle_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_org_id_sha256",
                table: "assets",
                columns: new[] { "org_id", "sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_id",
                table: "audit_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_org_id_at",
                table: "audit_events",
                columns: new[] { "org_id", "at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_config_versions_app_id_created_at",
                table: "config_versions",
                columns: new[] { "app_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_config_versions_content_address",
                table: "config_versions",
                columns: new[] { "app_id", "code_key", "asset_key", "content_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_config_versions_created_by",
                table: "config_versions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_org_members_user_id",
                table: "org_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_orgs_slug",
                table: "orgs",
                column: "slug",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_workspaces_org_id_slug",
                table: "workspaces",
                columns: new[] { "org_id", "slug" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_apps_config_versions_current_config_version_id",
                table: "apps",
                column: "current_config_version_id",
                principalTable: "config_versions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_apps_config_versions_current_config_version_id",
                table: "apps");

            migrationBuilder.DropTable(
                name: "assets");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "org_members");

            migrationBuilder.DropTable(
                name: "config_versions");

            migrationBuilder.DropTable(
                name: "apps");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "workspaces");

            migrationBuilder.DropTable(
                name: "orgs");
        }
    }
}
