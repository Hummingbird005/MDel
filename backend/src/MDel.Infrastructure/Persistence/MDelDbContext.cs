using MDel.Domain;
using MDel.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MDel.Infrastructure.Persistence;

/// <summary>
/// Contextul EF Core. Toate tabelele, cheile, relațiile, indecșii și constrângerile
/// sunt declarate aici prin ModelBuilder (entitățile din Domain nu au atribute).
///
/// Schema se generează cu:
///   dotnet ef migrations add &lt;Nume&gt; -p src/MDel.Infrastructure -s src/MDel.Api -o Persistence/Migrations
///   dotnet ef database update            -p src/MDel.Infrastructure -s src/MDel.Api
/// </summary>
public class MDelDbContext(DbContextOptions<MDelDbContext> options) : DbContext(options)
{
    public const string Schema = "mdel";

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Delegation> Delegations => Set<Delegation>();
    public DbSet<DelegationAttachment> DelegationAttachments => Set<DelegationAttachment>();
    public DbSet<DelegationNotification> DelegationNotifications => Set<DelegationNotification>();
    public DbSet<DelegationStatusHistory> DelegationStatusHistory => Set<DelegationStatusHistory>();
    public DbSet<InformGroupMember> InformGroupMembers => Set<InformGroupMember>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Toate datele se stochează în UTC; la citire marcăm explicit Kind = Utc.
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>()
            .HaveColumnType("datetime2(3)");
        configurationBuilder.Properties<DateTime?>()
            .HaveConversion<NullableUtcDateTimeConverter>()
            .HaveColumnType("datetime2(3)");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        ConfigureUsers(modelBuilder);
        ConfigureDelegations(modelBuilder);
        ConfigureAttachments(modelBuilder);
        ConfigureNotifications(modelBuilder);
        ConfigureStatusHistory(modelBuilder);
        ConfigureInformGroup(modelBuilder);
        ConfigureSettings(modelBuilder);
        ConfigureAudit(modelBuilder);
    }

    // ---------------------------------------------------------------- AppUsers

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.ToTable("AppUsers");
            e.HasKey(x => x.Id);

            e.Property(x => x.SamAccountName).HasMaxLength(256).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.PreferredLanguage).HasMaxLength(5).IsRequired().HasDefaultValue("ro");
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasIndex(x => x.SamAccountName).IsUnique();
            e.HasIndex(x => x.AdObjectId).IsUnique().HasFilter("[AdObjectId] IS NOT NULL");
            e.HasIndex(x => x.Email);

            e.ToTable(t => t.HasCheckConstraint("CK_AppUsers_PreferredLanguage", "[PreferredLanguage] IN ('ro','en')"));
        });
    }

    // ------------------------------------------------------------- Delegations

    private static void ConfigureDelegations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Delegation>(e =>
        {
            e.ToTable("Delegations", t =>
            {
                t.HasCheckConstraint("CK_Delegations_EndAfterStart", "[EndUtc] > [StartUtc]");
                t.HasCheckConstraint("CK_Delegations_DifferentUsers", "[FromUserId] <> [ToUserId]");
            });
            e.HasKey(x => x.Id);

            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.RequiresApproval).HasDefaultValue(true);
            e.Property(x => x.KeepCopyInSource).HasDefaultValue(true);
            e.Property(x => x.PreviousForwardingAddress).HasMaxLength(512);
            e.Property(x => x.AppliedForwardingAddress).HasMaxLength(512);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.RejectReason).HasMaxLength(1000);
            e.Property(x => x.StopReason).HasMaxLength(1000);
            e.Property(x => x.LastError).HasMaxLength(4000);
            e.Property(x => x.CreatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.RowVersion).IsRowVersion();

            // Relații către AppUser – Restrict: un utilizator cu istoric nu poate fi șters
            // (și evităm căile multiple de cascadă interzise de SQL Server).
            e.HasOne(x => x.FromUser).WithMany(u => u.DelegationsAsSource)
                .HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToUser).WithMany(u => u.DelegationsAsTarget)
                .HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedBy).WithMany(u => u.DelegationsCreated)
                .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ApprovedBy).WithMany(u => u.DelegationsApproved)
                .HasForeignKey(x => x.ApprovedById).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.StoppedBy).WithMany(u => u.DelegationsStopped)
                .HasForeignKey(x => x.StoppedById).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

            // Indecși pentru dashboard și scheduler.
            e.HasIndex(x => new { x.Status, x.StartUtc });
            e.HasIndex(x => new { x.Status, x.EndUtc });
            // Verificarea suprapunerilor pe aceeași căsuță sursă.
            e.HasIndex(x => new { x.FromUserId, x.Status, x.StartUtc, x.EndUtc });
            e.HasIndex(x => x.ToUserId);
            e.HasIndex(x => x.CreatedById);
        });
    }

    private static void ConfigureAttachments(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DelegationAttachment>(e =>
        {
            e.ToTable("DelegationAttachments", t =>
                t.HasCheckConstraint("CK_DelegationAttachments_Size", "[SizeBytes] >= 0"));
            e.HasKey(x => x.Id);

            e.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.StoredFileName).HasMaxLength(100).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
            e.Property(x => x.UploadedUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            // Delegare 1 – N atașamente; ștergerea delegării șterge înregistrările.
            e.HasOne(x => x.Delegation).WithMany(d => d.Attachments)
                .HasForeignKey(x => x.DelegationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.UploadedBy).WithMany(u => u.UploadedAttachments)
                .HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.StoredFileName).IsUnique();
            e.HasIndex(x => x.DelegationId);
            e.HasIndex(x => x.UploadedUtc); // job retenție
        });
    }

    private static void ConfigureNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DelegationNotification>(e =>
        {
            e.ToTable("DelegationNotifications");
            e.HasKey(x => x.Id);

            e.Property(x => x.RecipientEmail).HasMaxLength(320).IsRequired();
            e.Property(x => x.RecipientType).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Event).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Error).HasMaxLength(2000);
            e.Property(x => x.CreatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasOne(x => x.Delegation).WithMany(d => d.Notifications)
                .HasForeignKey(x => x.DelegationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RecipientUser).WithMany(u => u.Notifications)
                .HasForeignKey(x => x.RecipientUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.DelegationId, x.Event });
            e.HasIndex(x => x.SentUtc);
        });
    }

    private static void ConfigureStatusHistory(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DelegationStatusHistory>(e =>
        {
            e.ToTable("DelegationStatusHistory");
            e.HasKey(x => x.Id);

            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.ChangedUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasOne(x => x.Delegation).WithMany(d => d.StatusHistory)
                .HasForeignKey(x => x.DelegationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ChangedBy).WithMany(u => u.StatusChanges)
                .HasForeignKey(x => x.ChangedById).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.DelegationId, x.ChangedUtc });
        });
    }

    // ---------------------------------------------------------- Administrare

    private static void ConfigureInformGroup(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InformGroupMember>(e =>
        {
            e.ToTable("InformGroupMembers");
            e.HasKey(x => x.Id);
            e.Property(x => x.AddedUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            // 1 – 0..1: un utilizator apare cel mult o dată în grupul de informare.
            e.HasOne(x => x.User).WithOne(u => u.InformGroupMembership)
                .HasForeignKey<InformGroupMember>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AddedBy).WithMany()
                .HasForeignKey(x => x.AddedById).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.UserId).IsUnique();
        });
    }

    private static void ConfigureSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSetting>(e =>
        {
            e.ToTable("AppSettings");
            e.HasKey(x => x.Key);

            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.Value).HasMaxLength(4000);
            e.Property(x => x.Category).HasMaxLength(50).IsRequired();
            e.Property(x => x.UpdatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasOne(x => x.UpdatedBy).WithMany()
                .HasForeignKey(x => x.UpdatedById).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.Category);

            e.HasData(DefaultSettings.All);
        });
    }

    private static void ConfigureAudit(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);

            e.Property(x => x.UserName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.OldValues).HasColumnType("nvarchar(max)");
            e.Property(x => x.NewValues).HasColumnType("nvarchar(max)");
            e.Property(x => x.Details).HasMaxLength(4000);
            e.Property(x => x.HttpMethod).HasMaxLength(10);
            e.Property(x => x.Path).HasMaxLength(2048);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.Property(x => x.Result).HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.TimestampUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasOne(x => x.User).WithMany(u => u.AuditLogs)
                .HasForeignKey(x => x.UserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.TimestampUtc);
            e.HasIndex(x => new { x.UserId, x.TimestampUtc });
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.Action);
        });
    }

    // ------------------------------------------------------------ Append-only

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardAuditLog();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardAuditLog();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Jurnalul de audit nu se modifică și nu se șterge prin aplicație.
    /// Singura excepție este jobul de retenție, care folosește ExecuteDelete (în afara change tracker-ului).
    /// </summary>
    private void GuardAuditLog()
    {
        if (ChangeTracker.Entries<AuditLog>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("AuditLogs is append-only: entries cannot be modified or deleted.");
        }
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
