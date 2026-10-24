namespace ConstructionSaaS.Application.DTOs.Projects;

public class UpdateProjectDto
{
    public string ProjectCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? AddressLine1 { get; set; }
    public string? City { get; set; }
    public string? StateOrProvince { get; set; }
    public string? PostalCode { get; set; }
    public string Country { get; set; } = string.Empty;
    public decimal? Budget { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTime? PlannedStartDateUtc { get; set; }
    public DateTime? PlannedEndDateUtc { get; set; }
}