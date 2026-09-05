using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shellwright.Api.Data.Migrations
{
    /// <summary>
    /// Adds the one identity-free lookup the signed download endpoint needs.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than scaffolded. The SQL lives in
    /// <c>Data/Sql/ArtifactDownload.up.sql</c> and explains why a narrow
    /// <c>SECURITY DEFINER</c> function is preferable to any of the ways of
    /// giving the API a policy-bypassing connection.
    /// </remarks>
    public partial class ArtifactDownload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SqlResource.Read("ArtifactDownload.up.sql"));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(SqlResource.Read("ArtifactDownload.down.sql"));
    }
}
