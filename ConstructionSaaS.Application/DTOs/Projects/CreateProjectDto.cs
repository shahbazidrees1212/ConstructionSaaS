using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Application.DTOs.Projects;

public class CreateProjectDto
{
    [Range(1, int.MaxValue)]
    public int CompanyId { get; set; }

    [Required]
    [StringLength(50)]
    public string ProjectCode { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(200)]
    public string? AddressLine1 { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    public string? StateOrProvince { get; set; }

    [StringLength(20)]
    public string? PostalCode { get; set; }

    [Required]
    [StringLength(100)]
    public string Country { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal? Budget { get; set; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    [RegularExpression("^[A-Z]{3}$")]
    public string CurrencyCode { get; set; } = string.Empty;

    public DateTime? PlannedStartDateUtc { get; set; }
    public DateTime? PlannedEndDateUtc { get; set; }
}