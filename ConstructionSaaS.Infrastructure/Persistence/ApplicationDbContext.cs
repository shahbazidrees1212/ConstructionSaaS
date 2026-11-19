
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.Infrastructure.Persistence;

public partial class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ConstructionSaaS.Application.Interfaces.IAuditActor? auditActor = null,
        NotificationSignal? notificationSignal = null)
        : base(options)
    {
        _auditActor = auditActor;
        _notificationSignal = notificationSignal;
    }

    // ==========================================
    // DATABASE TABLES
    // ==========================================

    public DbSet<Company> Companies { get; set; }
    public DbSet<AccountProfile> AccountProfiles { get; set; }
    public DbSet<ProjectInvoice> ProjectInvoices { get; set; }

    public DbSet<Project> Projects { get; set; }

    public DbSet<ProjectAssignment> ProjectAssignments { get; set; }

    // NEW: Project Tasks
    public DbSet<WorkspaceNotification> WorkspaceNotifications { get; set; }
    public DbSet<SiteWorkEntry> SiteWorkEntries { get; set; }
    public DbSet<ProjectTask> ProjectTasks { get; set; }

    public DbSet<ProjectMilestone> ProjectMilestones { get; set; }

    public DbSet<ProjectDocument> ProjectDocuments { get; set; }

    public DbSet<WorkspaceActivity> WorkspaceActivities { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<AccountProfile>().HasOne<ApplicationUser>().WithOne()
            .HasForeignKey<AccountProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        ConfigureSubscriptions(modelBuilder);
        modelBuilder.Entity<ProjectInvoice>(e => {
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.ClientName).HasMaxLength(150).IsRequired();
            e.Property(x => x.ClientEmail).HasMaxLength(254).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(10).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.Property(x => x.LinkHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ProtectedLink).HasMaxLength(1000).IsRequired();
            e.HasIndex(x => x.LinkHash).IsUnique();
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.CheckoutId).HasMaxLength(255);
            e.Property(x => x.CheckoutUrl).HasMaxLength(2048);
            e.Property(x => x.PaymentReference).HasMaxLength(255);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.ProjectId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.Provider, x.CheckoutId }).IsUnique().HasFilter("[CheckoutId] IS NOT NULL");
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<InvoiceLine>(e => {
            e.Property(x => x.Description).HasMaxLength(250).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.HasOne(x => x.Invoice).WithMany(x => x.Lines).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
        });

        // ==========================================
        // PROJECT MILESTONES
        // ==========================================

        modelBuilder.Entity<ProjectMilestone>(e =>
        {
            e.Property(x => x.Title)
                .HasMaxLength(160)
                .IsRequired();

            e.Property(x => x.Notes)
                .HasMaxLength(2000);

            e.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new
            {
                x.ProjectId,
                x.DueDateUtc
            });
        });

        // ==========================================
        // PROJECT DOCUMENTS
        // ==========================================

        modelBuilder.Entity<ProjectDocument>(e =>
        {
            e.Property(x => x.FileName)
                .HasMaxLength(200)
                .IsRequired();

            e.Property(x => x.ContentType)
                .HasMaxLength(100)
                .IsRequired();

            e.Property(x => x.UploadedByUserId).HasMaxLength(450);
            e.Property(x => x.UploadedBy)
                .HasMaxLength(256)
                .IsRequired();

            e.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ==========================================
        // WORKSPACE ACTIVITIES
        // ==========================================

        modelBuilder.Entity<WorkspaceActivity>(e =>
        {
            e.Property(x => x.Category)
                .HasMaxLength(50)
                .IsRequired();

            e.Property(x => x.Description)
                .HasMaxLength(500)
                .IsRequired();

            e.Property(x => x.Actor)
                .HasMaxLength(256)
                .IsRequired();

            e.HasIndex(x => new
            {
                x.CompanyId,
                x.OccurredAtUtc
            });
        });

        // ==========================================
        // APPLICATION USER
        // ==========================================

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasOne(user => user.Company)
                .WithMany()
                .HasForeignKey(user => user.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ==========================================
        // COMPANY
        // ==========================================

        modelBuilder.Entity<Company>(entity =>
        {
            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.LegalName)
                .HasMaxLength(250);

            entity.Property(x => x.RegistrationNumber)
                .HasMaxLength(50);

            entity.Property(x => x.Email)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(x => x.PhoneNumber)
                .HasMaxLength(30);

            entity.Property(x => x.Website)
                .HasMaxLength(500);

            entity.Property(x => x.AddressLine1)
                .HasMaxLength(200);

            entity.Property(x => x.AddressLine2)
                .HasMaxLength(200);

            entity.Property(x => x.City)
                .HasMaxLength(100);

            entity.Property(x => x.StateOrProvince)
                .HasMaxLength(100);

            entity.Property(x => x.PostalCode)
                .HasMaxLength(20);

            entity.Property(x => x.Country)
                .IsRequired()
                .HasMaxLength(100);
        });

        // ==========================================
        // PROJECT
        // ==========================================

        modelBuilder.Entity<Project>(entity =>
        {
            entity.Property(x => x.ProjectCode)
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.Budget)
                .HasPrecision(18, 2);

            entity.Property(x => x.CurrencyCode)
                .IsRequired()
                .HasMaxLength(3);

            entity.Property(x => x.Country)
                .IsRequired()
                .HasMaxLength(100);

            entity.HasOne(x => x.Company)
                .WithMany(x => x.Projects)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.CompanyId,
                x.ProjectCode
            })
            .IsUnique();
        });

        // ==========================================
        // PROJECT TASKS (NEW MODULE)
        // ==========================================

        modelBuilder.Entity<SiteWorkEntry>(e => {
            e.Property(x => x.AuthorUserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.AuthorEmail).HasMaxLength(256).IsRequired();
            e.Property(x => x.Kind).HasMaxLength(30).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(3000).IsRequired();
            e.Property(x => x.Blockers).HasMaxLength(2000);
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.ProjectId, x.AuthorUserId, x.ReportDate }).IsUnique().HasFilter("[TaskId] IS NULL AND [ReportDate] IS NOT NULL");
        });
        modelBuilder.Entity<WorkspaceNotification>(e => {
            e.Property(n => n.RecipientUserId).HasMaxLength(450).IsRequired();
            e.Property(n => n.Message).HasMaxLength(500).IsRequired();
            e.HasIndex(n => new { n.CompanyId, n.RecipientUserId, n.CreatedAtUtc });
        });
        modelBuilder.Entity<WorkspaceActivity>().HasIndex(a => new { a.CompanyId, a.ProjectId, a.OccurredAtUtc });
        modelBuilder.Entity<ProjectTask>().Property(t => t.Status).IsConcurrencyToken();
        modelBuilder.Entity<ProjectTask>(entity =>
        {
            entity.ToTable("ProjectTasks");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(x => x.Description)
                .HasMaxLength(2000);

            entity.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(30);

            entity.Property(x => x.Priority)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(x => x.AssignedToUserId)
                .HasMaxLength(450);

            // One Project -> Many Tasks
            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // One Identity User -> Many Tasks
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.AssignedToUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Tenant and project lookup
            entity.HasIndex(x => new
            {
                x.CompanyId,
                x.ProjectId
            });

            // Find tasks assigned to a user
            entity.HasIndex(x => x.AssignedToUserId);
        });

        // ==========================================
        // PROJECT ASSIGNMENTS
        // ==========================================

        modelBuilder.Entity<ProjectAssignment>(entity =>
        {
            entity.ToTable("ProjectAssignments");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.UserId)
                .IsRequired()
                .HasMaxLength(450);

            entity.Property(x => x.AssignedAtUtc)
                .IsRequired();

            // One Project -> Many Assignments
            entity.HasOne(x => x.Project)
                .WithMany(x => x.Assignments)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // One Identity User -> Many Assignments
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Prevent duplicate assignments
            entity.HasIndex(x => new
            {
                x.ProjectId,
                x.UserId
            })
            .IsUnique();

            // Efficient queries by employee
            entity.HasIndex(x => x.UserId);
        });
    }
}
