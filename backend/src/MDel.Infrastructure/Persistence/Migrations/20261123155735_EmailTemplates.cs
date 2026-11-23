using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MDel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmailTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "mdel",
                table: "AppSettings",
                columns: new[] { "Key", "Category", "IsSecret", "UpdatedById", "UpdatedUtc", "Value" },
                values: new object[,]
                {
                    { "Templates.Activated.Body", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Forwardul a fost aplicat în Exchange și este activ până la {sfarsit}." },
                    { "Templates.Activated.Subject", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "[MDel] Forward activ: {sursa} → {destinatar} (#{id})" },
                    { "Templates.Approved.Body", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Cererea de forward a fost aprobată de {aprobat_de}." },
                    { "Templates.Approved.Subject", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "[MDel] Forward aprobat: {sursa} → {destinatar} (#{id})" },
                    { "Templates.Direct.Body", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "A fost înregistrat un forward fără aprobare. Mesajul este doar informativ." },
                    { "Templates.Direct.Subject", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "[MDel] Forward înregistrat fără aprobare: {sursa} → {destinatar} (#{id})" },
                    { "Templates.Expired.Body", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Perioada s-a încheiat, iar forwardul a fost eliminat automat din Exchange." },
                    { "Templates.Expired.Subject", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "[MDel] Forward expirat: {sursa} → {destinatar} (#{id})" },
                    { "Templates.Failed.Body", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Operația în Exchange a eșuat și necesită intervenția unui administrator." },
                    { "Templates.Failed.Subject", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "[MDel] Eroare forward: {sursa} → {destinatar} (#{id})" },
                    { "Templates.Pending.Body", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "A fost înregistrată o cerere de forward, aflată în așteptarea aprobării. Mesajul este doar informativ." },
                    { "Templates.Pending.Subject", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "[MDel] Cerere de forward înregistrată: {sursa} → {destinatar} (#{id})" },
                    { "Templates.Rejected.Body", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Cererea de forward a fost respinsă." },
                    { "Templates.Rejected.Subject", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "[MDel] Forward respins: {sursa} → {destinatar} (#{id})" },
                    { "Templates.Request.Body", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "A fost înregistrată o cerere de forward care necesită aprobarea dumneavoastră. Este suficientă aprobarea unui singur membru al grupului." },
                    { "Templates.Request.Subject", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "[MDel] Cerere de aprobare: forward {sursa} → {destinatar} (#{id})" },
                    { "Templates.Stopped.Body", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Forwardul a fost oprit de un administrator." },
                    { "Templates.Stopped.Subject", "Templates", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "[MDel] Forward oprit: {sursa} → {destinatar} (#{id})" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Activated.Body");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Activated.Subject");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Approved.Body");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Approved.Subject");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Direct.Body");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Direct.Subject");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Expired.Body");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Expired.Subject");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Failed.Body");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Failed.Subject");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Pending.Body");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Pending.Subject");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Rejected.Body");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Rejected.Subject");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Request.Body");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Request.Subject");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Stopped.Body");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Templates.Stopped.Subject");
        }
    }
}
