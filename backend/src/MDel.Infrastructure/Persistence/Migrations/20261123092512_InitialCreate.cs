using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MDel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "mdel");

            migrationBuilder.CreateTable(
                name: "AppUsers",
                schema: "mdel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SamAccountName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    PreferredLanguage = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false, defaultValue: "ro"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    LastLoginUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                    table.CheckConstraint("CK_AppUsers_PreferredLanguage", "[PreferredLanguage] IN ('ro','en')");
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                schema: "mdel",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsSecret = table.Column<bool>(type: "bit", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UpdatedById = table.Column<int>(type: "int", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                    table.ForeignKey(
                        name: "FK_AppSettings_AppUsers_UpdatedById",
                        column: x => x.UpdatedById,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                schema: "mdel",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TimestampUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    HttpMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Path = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Result = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_AppUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Delegations",
                schema: "mdel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromUserId = table.Column<int>(type: "int", nullable: false),
                    ToUserId = table.Column<int>(type: "int", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequiresApproval = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    KeepCopyInSource = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    PreviousForwardingAddress = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    PreviousDeliverToMailboxAndForward = table.Column<bool>(type: "bit", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ApprovedById = table.Column<int>(type: "int", nullable: true),
                    ApprovedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StoppedById = table.Column<int>(type: "int", nullable: true),
                    StoppedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    StopReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExchangeAppliedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    ExchangeRemovedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delegations", x => x.Id);
                    table.CheckConstraint("CK_Delegations_DifferentUsers", "[FromUserId] <> [ToUserId]");
                    table.CheckConstraint("CK_Delegations_EndAfterStart", "[EndUtc] > [StartUtc]");
                    table.ForeignKey(
                        name: "FK_Delegations_AppUsers_ApprovedById",
                        column: x => x.ApprovedById,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Delegations_AppUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Delegations_AppUsers_FromUserId",
                        column: x => x.FromUserId,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Delegations_AppUsers_StoppedById",
                        column: x => x.StoppedById,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Delegations_AppUsers_ToUserId",
                        column: x => x.ToUserId,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InformGroupMembers",
                schema: "mdel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    AddedById = table.Column<int>(type: "int", nullable: false),
                    AddedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InformGroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InformGroupMembers_AppUsers_AddedById",
                        column: x => x.AddedById,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InformGroupMembers_AppUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DelegationAttachments",
                schema: "mdel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DelegationId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    StoredFileName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    UploadedById = table.Column<int>(type: "int", nullable: false),
                    UploadedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    PurgedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationAttachments", x => x.Id);
                    table.CheckConstraint("CK_DelegationAttachments_Size", "[SizeBytes] >= 0");
                    table.ForeignKey(
                        name: "FK_DelegationAttachments_AppUsers_UploadedById",
                        column: x => x.UploadedById,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DelegationAttachments_Delegations_DelegationId",
                        column: x => x.DelegationId,
                        principalSchema: "mdel",
                        principalTable: "Delegations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DelegationNotifications",
                schema: "mdel",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DelegationId = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: true),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    RecipientType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Event = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    SentUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegationNotifications_AppUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DelegationNotifications_Delegations_DelegationId",
                        column: x => x.DelegationId,
                        principalSchema: "mdel",
                        principalTable: "Delegations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DelegationStatusHistory",
                schema: "mdel",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DelegationId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ChangedById = table.Column<int>(type: "int", nullable: true),
                    ChangedUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DelegationStatusHistory_AppUsers_ChangedById",
                        column: x => x.ChangedById,
                        principalSchema: "mdel",
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DelegationStatusHistory_Delegations_DelegationId",
                        column: x => x.DelegationId,
                        principalSchema: "mdel",
                        principalTable: "Delegations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "mdel",
                table: "AppSettings",
                columns: new[] { "Key", "Category", "IsSecret", "UpdatedById", "UpdatedUtc", "Value" },
                values: new object[,]
                {
                    { "Ad.BaseDn", "ActiveDirectory", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Ad.Domain", "ActiveDirectory", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Ad.LdapServer", "ActiveDirectory", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Ad.ServicePassword", "ActiveDirectory", true, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "Ad.ServiceUser", "ActiveDirectory", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Attachments.AllowedExtensions", "Attachments", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), ".pdf,.doc,.docx,.xls,.xlsx,.msg,.eml,.txt,.png,.jpg,.jpeg,.zip" },
                    { "Attachments.MaxSizeMb", "Attachments", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "100" },
                    { "Attachments.StoragePath", "Attachments", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Exchange.KeepCopyInSource", "Exchange", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "Exchange.PowerShellUri", "Exchange", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Exchange.ServicePassword", "Exchange", true, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "Exchange.ServiceUser", "Exchange", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "General.DefaultLanguage", "General", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "ro" },
                    { "General.PreventSelfApproval", "General", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "true" },
                    { "Groups.Admins", "Groups", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Groups.AdminsEmail", "Groups", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Groups.Approvers", "Groups", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Groups.ApproversEmail", "Groups", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Retention.AttachmentsDays", "Retention", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "365" },
                    { "Retention.AuditDays", "Retention", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "365" },
                    { "Smtp.FromAddress", "Smtp", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Smtp.Host", "Smtp", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Smtp.Password", "Smtp", true, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { "Smtp.Port", "Smtp", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "25" },
                    { "Smtp.User", "Smtp", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "" },
                    { "Smtp.UseTls", "Smtp", false, null, new DateTime(2026, 11, 23, 0, 0, 0, 0, DateTimeKind.Utc), "true" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_Category",
                schema: "mdel",
                table: "AppSettings",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_UpdatedById",
                schema: "mdel",
                table: "AppSettings",
                column: "UpdatedById");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_AdObjectId",
                schema: "mdel",
                table: "AppUsers",
                column: "AdObjectId",
                unique: true,
                filter: "[AdObjectId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_Email",
                schema: "mdel",
                table: "AppUsers",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_SamAccountName",
                schema: "mdel",
                table: "AppUsers",
                column: "SamAccountName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Action",
                schema: "mdel",
                table: "AuditLogs",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                schema: "mdel",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TimestampUtc",
                schema: "mdel",
                table: "AuditLogs",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_TimestampUtc",
                schema: "mdel",
                table: "AuditLogs",
                columns: new[] { "UserId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationAttachments_DelegationId",
                schema: "mdel",
                table: "DelegationAttachments",
                column: "DelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationAttachments_StoredFileName",
                schema: "mdel",
                table: "DelegationAttachments",
                column: "StoredFileName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DelegationAttachments_UploadedById",
                schema: "mdel",
                table: "DelegationAttachments",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationAttachments_UploadedUtc",
                schema: "mdel",
                table: "DelegationAttachments",
                column: "UploadedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationNotifications_DelegationId_Event",
                schema: "mdel",
                table: "DelegationNotifications",
                columns: new[] { "DelegationId", "Event" });

            migrationBuilder.CreateIndex(
                name: "IX_DelegationNotifications_RecipientUserId",
                schema: "mdel",
                table: "DelegationNotifications",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationNotifications_SentUtc",
                schema: "mdel",
                table: "DelegationNotifications",
                column: "SentUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_ApprovedById",
                schema: "mdel",
                table: "Delegations",
                column: "ApprovedById");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_CreatedById",
                schema: "mdel",
                table: "Delegations",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_FromUserId_Status_StartUtc_EndUtc",
                schema: "mdel",
                table: "Delegations",
                columns: new[] { "FromUserId", "Status", "StartUtc", "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_Status_EndUtc",
                schema: "mdel",
                table: "Delegations",
                columns: new[] { "Status", "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_Status_StartUtc",
                schema: "mdel",
                table: "Delegations",
                columns: new[] { "Status", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_StoppedById",
                schema: "mdel",
                table: "Delegations",
                column: "StoppedById");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_ToUserId",
                schema: "mdel",
                table: "Delegations",
                column: "ToUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationStatusHistory_ChangedById",
                schema: "mdel",
                table: "DelegationStatusHistory",
                column: "ChangedById");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationStatusHistory_DelegationId_ChangedUtc",
                schema: "mdel",
                table: "DelegationStatusHistory",
                columns: new[] { "DelegationId", "ChangedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InformGroupMembers_AddedById",
                schema: "mdel",
                table: "InformGroupMembers",
                column: "AddedById");

            migrationBuilder.CreateIndex(
                name: "IX_InformGroupMembers_UserId",
                schema: "mdel",
                table: "InformGroupMembers",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings",
                schema: "mdel");

            migrationBuilder.DropTable(
                name: "AuditLogs",
                schema: "mdel");

            migrationBuilder.DropTable(
                name: "DelegationAttachments",
                schema: "mdel");

            migrationBuilder.DropTable(
                name: "DelegationNotifications",
                schema: "mdel");

            migrationBuilder.DropTable(
                name: "DelegationStatusHistory",
                schema: "mdel");

            migrationBuilder.DropTable(
                name: "InformGroupMembers",
                schema: "mdel");

            migrationBuilder.DropTable(
                name: "Delegations",
                schema: "mdel");

            migrationBuilder.DropTable(
                name: "AppUsers",
                schema: "mdel");
        }
    }
}
