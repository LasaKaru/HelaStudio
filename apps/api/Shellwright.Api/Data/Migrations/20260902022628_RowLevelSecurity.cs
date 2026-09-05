using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shellwright.Api.Data.Migrations
{
    /// <summary>
    /// Turns on tenant isolation: helper functions, per-table policies, and the
    /// narrow grant set the application role runs with.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than scaffolded, because none of it is expressible
    /// in the model builder. The SQL lives in
    /// <c>Data/Sql/RowLevelSecurity.up.sql</c> and its reverse alongside it.
    /// </remarks>
    public partial class RowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SqlResource.Read("RowLevelSecurity.up.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SqlResource.Read("RowLevelSecurity.down.sql"));
    }
}
