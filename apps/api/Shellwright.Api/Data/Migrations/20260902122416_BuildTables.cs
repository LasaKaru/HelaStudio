using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shellwright.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BuildTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "artifact_cache",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    code_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    asset_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    artifact_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    artifact_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artifact_cache", x => x.id);
                    table.ForeignKey(
                        name: "fk_artifact_cache_apps_app_id",
                        column: x => x.app_id,
                        principalTable: "apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "builds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    workflow_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    artifact_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    artifact_bytes = table.Column<long>(type: "bigint", nullable: true),
                    cache_outcome = table.Column<int>(type: "integer", nullable: false),
                    runner_seconds = table.Column<int>(type: "integer", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_builds", x => x.id);
                    table.ForeignKey(
                        name: "fk_builds_apps_app_id",
                        column: x => x.app_id,
                        principalTable: "apps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_builds_config_versions_config_version_id",
                        column: x => x.config_version_id,
                        principalTable: "config_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_builds_orgs_org_id",
                        column: x => x.org_id,
                        principalTable: "orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_builds_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "build_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_build_transitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_build_transitions_builds_build_id",
                        column: x => x.build_id,
                        principalTable: "builds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usage_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    build_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    runner_seconds = table.Column<int>(type: "integer", nullable: false),
                    cache_hit = table.Column<bool>(type: "boolean", nullable: false),
                    artifact_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_usage_records_builds_build_id",
                        column: x => x.build_id,
                        principalTable: "builds",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_usage_records_orgs_org_id",
                        column: x => x.org_id,
                        principalTable: "orgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_artifact_cache_code_key",
                table: "artifact_cache",
                columns: new[] { "app_id", "platform", "type", "code_key" });

            migrationBuilder.CreateIndex(
                name: "ix_artifact_cache_keys",
                table: "artifact_cache",
                columns: new[] { "app_id", "platform", "type", "code_key", "asset_key", "content_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_build_transitions_build_id_occurred_at",
                table: "build_transitions",
                columns: new[] { "build_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_builds_app_id_created_at",
                table: "builds",
                columns: new[] { "app_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_builds_config_version_id",
                table: "builds",
                column: "config_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_builds_idempotency",
                table: "builds",
                columns: new[] { "app_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_builds_org_id",
                table: "builds",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "ix_builds_requested_by",
                table: "builds",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_builds_workflow_id",
                table: "builds",
                column: "workflow_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_build",
                table: "usage_records",
                column: "build_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_org_id_created_at",
                table: "usage_records",
                columns: new[] { "org_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artifact_cache");

            migrationBuilder.DropTable(
                name: "build_transitions");

            migrationBuilder.DropTable(
                name: "usage_records");

            migrationBuilder.DropTable(
                name: "builds");
        }
    }
}
