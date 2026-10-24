namespace ConstructionSaaS.Application.DTOs.Projects;

public class ProjectDto
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string ProjectCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public decimal? Budget { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public DateTime? PlannedStartDateUtc { get; set; }
    public DateTime? PlannedEndDateUtc { get; set; }
}