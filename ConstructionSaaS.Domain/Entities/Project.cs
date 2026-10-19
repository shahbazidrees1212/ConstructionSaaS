using ConstructionSaaS.Domain.Enums;

namespace ConstructionSaaS.Domain.Entities;

public class Project
{
    public int Id { get; set; }

    // Project kis company ka hai
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public string ProjectCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Construction site ka address
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? StateOrProvince { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = string.Empty;

    public decimal? Budget { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;

    public DateTime? PlannedStartDateUtc { get; set; }
    public DateTime? PlannedEndDateUtc { get; set; }
    public DateTime? ActualStartDateUtc { get; set; }
    public DateTime? ActualEndDateUtc { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Planning;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}