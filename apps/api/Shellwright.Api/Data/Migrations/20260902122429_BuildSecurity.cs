using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shellwright.Api.Data.Migrations
{
    /// <summary>
    /// Extends tenant isolation over the build tables: policies, and the narrow
    /// grant set each one actually needs.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than scaffolded, because none of it is expressible in
    /// the model builder. The SQL lives in <c>Data/Sql/BuildSecurity.up.sql</c>
    /// and its reverse alongside it.
    /// </remarks>
    public partial class BuildSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SqlResource.Read("BuildSecurity.up.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SqlResource.Read("BuildSecurity.down.sql"));
    }
}
