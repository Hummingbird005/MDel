using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SystemAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "mdel",
                table: "AppSettings",
                columns: new[] { "Key", "Category", "IsSecret", "UpdatedById", "UpdatedUtc", "Value" },
                values: new object[] { "Groups.SystemAdmins", "Groups", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Groups.SystemAdmins");
        }
    }
}
