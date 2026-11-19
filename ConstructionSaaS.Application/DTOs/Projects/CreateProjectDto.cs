using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Application.DTOs.Projects;

public class CreateProjectDto : IValidatableObject
{
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
    [RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency code must be three letters.")]
    public string CurrencyCode { get; set; } = string.Empty;

    public DateTime? PlannedStartDateUtc { get; set; }
    public DateTime? PlannedEndDateUtc { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PlannedStartDateUtc.HasValue &&
            PlannedEndDateUtc.HasValue &&
            PlannedEndDateUtc < PlannedStartDateUtc)
        {
            yield return new ValidationResult(
                "Planned end date must be on or after the start date.",
                new[] { nameof(PlannedEndDateUtc) });
        }
    }
}