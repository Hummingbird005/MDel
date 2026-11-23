using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MDel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalAndNotificationOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DelegationNotifications_SentUtc",
                schema: "mdel",
                table: "DelegationNotifications");

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                schema: "mdel",
                table: "DelegationNotifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HtmlBody",
                schema: "mdel",
                table: "DelegationNotifications",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                schema: "mdel",
                table: "DelegationNotifications",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "ro");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptUtc",
                schema: "mdel",
                table: "DelegationNotifications",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                schema: "mdel",
                table: "DelegationNotifications",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.InsertData(
                schema: "mdel",
                table: "AppSettings",
                columns: new[] { "Key", "Category", "IsSecret", "UpdatedById", "UpdatedUtc", "Value" },
                values: new object[] { "General.BaseUrl", "General", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationNotifications_SentUtc_Attempts",
                schema: "mdel",
                table: "DelegationNotifications",
                columns: new[] { "SentUtc", "Attempts" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DelegationNotifications_SentUtc_Attempts",
                schema: "mdel",
                table: "DelegationNotifications");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "General.BaseUrl");

            migrationBuilder.DropColumn(
                name: "Attempts",
                schema: "mdel",
                table: "DelegationNotifications");

            migrationBuilder.DropColumn(
                name: "HtmlBody",
                schema: "mdel",
                table: "DelegationNotifications");

            migrationBuilder.DropColumn(
                name: "Language",
                schema: "mdel",
                table: "DelegationNotifications");

            migrationBuilder.DropColumn(
                name: "LastAttemptUtc",
                schema: "mdel",
                table: "DelegationNotifications");

            migrationBuilder.DropColumn(
                name: "Subject",
                schema: "mdel",
                table: "DelegationNotifications");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationNotifications_SentUtc",
                schema: "mdel",
                table: "DelegationNotifications",
                column: "SentUtc");
        }
    }
}
