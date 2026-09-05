using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shellwright.Api.Data.Migrations
{
    /// <summary>
    /// Grants the build orchestrator the narrow reach it needs, without
    /// <c>BYPASSRLS</c>.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than scaffolded. The SQL lives in
    /// <c>Data/Sql/RunnerRole.up.sql</c> and explains why every line of it is
    /// scoped <c>TO shellwright_runner</c>.
    /// </remarks>
    public partial class RunnerRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SqlResource.Read("RunnerRole.up.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SqlResource.Read("RunnerRole.down.sql"));
    }
}
