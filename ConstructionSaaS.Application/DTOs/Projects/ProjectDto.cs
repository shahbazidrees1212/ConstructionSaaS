
namespace ConstructionSaaS.Application.DTOs.Projects;

public class ProjectDto
{
    // Project Identity
    public int Id { get; set; }
    public int CompanyId { get; set; }

    // Basic Information
    public string ProjectCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Project Location
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? StateOrProvince { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = string.Empty;

    // Budget Information
    public decimal? Budget { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;

    // Planned Timeline
    public DateTime? PlannedStartDateUtc { get; set; }
    public DateTime? PlannedEndDateUtc { get; set; }

    // Actual Timeline
    public DateTime? ActualStartDateUtc { get; set; }
    public DateTime? ActualEndDateUtc { get; set; }

    // Project Status
    public string Status { get; set; } = "Planning";

    // Audit Information
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
