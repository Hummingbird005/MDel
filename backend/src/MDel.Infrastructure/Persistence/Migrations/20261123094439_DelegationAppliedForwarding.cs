using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DelegationAppliedForwarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AppliedForwardingAddress",
                schema: "mdel",
                table: "Delegations",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppliedForwardingAddress",
                schema: "mdel",
                table: "Delegations");
        }
    }
}
