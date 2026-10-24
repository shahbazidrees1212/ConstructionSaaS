using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.Infrastructure.Persistence;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    public DbSet<Company> Companies { get; set; }
        public DbSet<Project> Projects { get; set; } 

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
        modelBuilder.Entity<ApplicationUser>()
    .HasOne(user => user.Company)
    .WithMany()
    .HasForeignKey(user => user.CompanyId)
    .OnDelete(DeleteBehavior.Restrict);
        base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Company>(entity =>
            {
                entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
                entity.Property(x => x.LegalName).HasMaxLength(250);
                entity.Property(x => x.RegistrationNumber).HasMaxLength(50);
                entity.Property(x => x.Email).IsRequired().HasMaxLength(256);
                entity.Property(x => x.PhoneNumber).HasMaxLength(30);
                entity.Property(x => x.Website).HasMaxLength(500);
                entity.Property(x => x.AddressLine1).HasMaxLength(200);
                entity.Property(x => x.AddressLine2).HasMaxLength(200);
                entity.Property(x => x.City).HasMaxLength(100);
                entity.Property(x => x.StateOrProvince).HasMaxLength(100);
                entity.Property(x => x.PostalCode).HasMaxLength(20);
                entity.Property(x => x.Country).IsRequired().HasMaxLength(100);
            });

            modelBuilder.Entity<Project>(entity =>
            {
                entity.Property(x => x.ProjectCode).IsRequired().HasMaxLength(50);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(200);
                entity.Property(x => x.Description).HasMaxLength(2000);
                entity.Property(x => x.Budget).HasPrecision(18, 2);
                entity.Property(x => x.CurrencyCode).IsRequired().HasMaxLength(3);
                entity.Property(x => x.Country).IsRequired().HasMaxLength(100);

                entity.HasOne(x => x.Company)
                    .WithMany(x => x.Projects)
                    .HasForeignKey(x => x.CompanyId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.CompanyId, x.ProjectCode })
                    .IsUnique();
            });
        }
    }
