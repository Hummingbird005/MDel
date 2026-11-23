using MDel.Domain.Entities;
using MDel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MDel.Tests;

/// <summary>Verifică modelul EF definit în OnModelCreating (fără conexiune la SQL Server).</summary>
public class ModelTests
{
    private static MDelDbContext CreateSqlServerContext() =>
        new(new DbContextOptionsBuilder<MDelDbContext>()
            .UseSqlServer("Server=unused;Database=unused;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", MDelDbContext.Schema))
            .Options);

    [Fact]
    public void Migrations_are_in_sync_with_the_model()
    {
        using var db = CreateSqlServerContext();
        Assert.False(db.Database.HasPendingModelChanges(),
            "Modelul s-a schimbat: rulați 'dotnet ef migrations add <Nume>'.");
    }

    [Fact]
    public void Create_script_contains_all_tables()
    {
        using var db = CreateSqlServerContext();
        var script = db.Database.GenerateCreateScript();

        foreach (var table in new[]
                 {
                     "AppUsers", "Delegations", "DelegationAttachments", "DelegationNotifications",
                     "DelegationStatusHistory", "InformGroupMembers", "AppSettings", "AuditLogs",
                 })
        {
            Assert.Contains($"[mdel].[{table}]", script);
        }

        Assert.Contains("CK_Delegations_EndAfterStart", script);
        Assert.Contains("CK_Delegations_DifferentUsers", script);
    }

    [Fact]
    public void User_relationships_are_restrict_and_children_cascade()
    {
        using var db = CreateSqlServerContext();
        var delegation = db.Model.FindEntityType(typeof(Delegation))!;

        var userFks = delegation.GetForeignKeys().Where(fk => fk.PrincipalEntityType.ClrType == typeof(AppUser)).ToList();
        Assert.Equal(5, userFks.Count); // From, To, CreatedBy, ApprovedBy, StoppedBy
        Assert.All(userFks, fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));

        foreach (var child in new[] { typeof(DelegationAttachment), typeof(DelegationNotification), typeof(DelegationStatusHistory) })
        {
            var fk = db.Model.FindEntityType(child)!.GetForeignKeys().Single(f => f.PrincipalEntityType == delegation);
            Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior);
        }
    }

    [Fact]
    public void Inform_group_member_is_unique_per_user()
    {
        using var db = CreateSqlServerContext();
        var entity = db.Model.FindEntityType(typeof(InformGroupMember))!;
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Single().Name == nameof(InformGroupMember.UserId));
    }

    [Fact]
    public void Default_settings_include_one_year_retention_and_100mb_limit()
    {
        using var db = CreateSqlServerContext();
        var designModel = db.GetService<IDesignTimeModel>().Model;
        var seed = designModel.FindEntityType(typeof(AppSetting))!.GetSeedData()
            .ToDictionary(d => (string)d[nameof(AppSetting.Key)]!, d => d[nameof(AppSetting.Value)] as string);

        Assert.Equal("365", seed["Retention.AttachmentsDays"]);
        Assert.Equal("365", seed["Retention.AuditDays"]);
        Assert.Equal("100", seed["Attachments.MaxSizeMb"]);
        Assert.Equal("true", seed["Exchange.KeepCopyInSource"]);
    }

    [Fact]
    public void Audit_log_is_append_only()
    {
        using var db = new MDelDbContext(new DbContextOptionsBuilder<MDelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.AuditLogs.Add(new AuditLog { Action = "x", UserName = "u", TimestampUtc = DateTime.UtcNow });
        db.SaveChanges();

        var log = db.AuditLogs.Single();
        log.Action = "changed";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }
}
