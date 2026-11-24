using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MDel.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Configurarea AD strict din Setări: câmpuri noi (port, SSL, validarea certificatului, OU-uri de căutare, în locul
    /// Base DN), iar utilizatorii AD / Exchange / SMTP devin secrete. Valorile existente se păstrează:
    /// la prima pornire după actualizare, aplicația le criptează (vezi SettingsService.EncryptPlainSecretsAsync).
    /// </summary>
    public partial class AdSettingsFromUi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Ad.ServiceUser",
                column: "IsSecret",
                value: true);

            migrationBuilder.UpdateData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Exchange.ServiceUser",
                column: "IsSecret",
                value: true);

            migrationBuilder.UpdateData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Smtp.User",
                column: "IsSecret",
                value: true);

            migrationBuilder.InsertData(
                schema: "mdel",
                table: "AppSettings",
                columns: new[] { "Key", "Category", "IsSecret", "UpdatedById", "UpdatedUtc", "Value" },
                values: new object[,]
                {
                    { "Ad.Port", "ActiveDirectory", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Ad.SearchBases", "ActiveDirectory", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Ad.UseSsl", "ActiveDirectory", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "false" },
                    { "Ad.ValidateCertificate", "ActiveDirectory", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "true" }
                });

            // Base DN-ul salvat înainte devine primul OU de căutare.
            migrationBuilder.Sql(
                "UPDATE s SET s.[Value] = b.[Value] FROM [mdel].[AppSettings] s " +
                "JOIN [mdel].[AppSettings] b ON b.[Key] = 'Ad.BaseDn' " +
                "WHERE s.[Key] = 'Ad.SearchBases' AND b.[Value] IS NOT NULL AND b.[Value] <> ''");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Ad.BaseDn");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Ad.Port");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Ad.SearchBases");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Ad.UseSsl");

            migrationBuilder.DeleteData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Ad.ValidateCertificate");

            migrationBuilder.UpdateData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Ad.ServiceUser",
                columns: new[] { "IsSecret", "Value" },
                values: new object[] { false, "" });

            migrationBuilder.UpdateData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Exchange.ServiceUser",
                columns: new[] { "IsSecret", "Value" },
                values: new object[] { false, "" });

            migrationBuilder.UpdateData(
                schema: "mdel",
                table: "AppSettings",
                keyColumn: "Key",
                keyValue: "Smtp.User",
                columns: new[] { "IsSecret", "Value" },
                values: new object[] { false, "" });

            migrationBuilder.InsertData(
                schema: "mdel",
                table: "AppSettings",
                columns: new[] { "Key", "Category", "IsSecret", "UpdatedById", "UpdatedUtc", "Value" },
                values: new object[] { "Ad.BaseDn", "ActiveDirectory", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" });
        }
    }
}
