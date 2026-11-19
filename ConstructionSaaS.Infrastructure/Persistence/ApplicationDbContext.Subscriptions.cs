using ConstructionSaaS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.Infrastructure.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<CompanySubscription> CompanySubscriptions { get; set; }
    public DbSet<SubscriptionPayment> SubscriptionPayments { get; set; }
    private static void ConfigureSubscriptions(ModelBuilder model)
    {
        model.Entity<CompanySubscription>(e => {
            e.HasKey(x => x.CompanyId);
            e.HasOne(x => x.Company).WithOne().HasForeignKey<CompanySubscription>(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.AttemptId).IsUnique();
            e.HasIndex(x => new { x.Provider, x.IsSandbox, x.ProviderSubscriptionId }).IsUnique().HasFilter("[ProviderSubscriptionId] IS NOT NULL");
            e.Property(x => x.PlanKey).HasMaxLength(60);
            e.Property(x => x.PlanName).HasMaxLength(120);
            e.Property(x => x.Interval).HasMaxLength(10);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Provider).HasMaxLength(10);
            e.Property(x => x.ProviderPlanId).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(40);
            e.Property(x => x.CheckoutId).HasMaxLength(200);
            e.Property(x => x.CheckoutUrl).HasMaxLength(2048);
            e.Property(x => x.ProviderSubscriptionId).HasMaxLength(200);
            e.Property(x => x.CustomerId).HasMaxLength(200);
            e.Property(x => x.Version).IsRowVersion();
        });
        model.Entity<SubscriptionPayment>(e => {
            e.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Provider, x.IsSandbox, x.Reference }).IsUnique();
            e.Property(x => x.Provider).HasMaxLength(10);
            e.Property(x => x.Reference).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Amount).HasPrecision(18, 2);
        });
    }
}
