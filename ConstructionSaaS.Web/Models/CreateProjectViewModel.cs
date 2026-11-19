
using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Web.Models;

public class CreateProjectViewModel : IValidatableObject
{
    [Required]
    [StringLength(50)]
    [Display(Name = "Project Code")]
    public string ProjectCode { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    [Display(Name = "Project Name")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(200)]
    [Display(Name = "Address")]
    public string? AddressLine1 { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(100)]
    [Display(Name = "State / Province")]
    public string? StateOrProvince { get; set; }

    [StringLength(20)]
    [Display(Name = "Postal Code")]
    public string? PostalCode { get; set; }

    [Required]
    [StringLength(100)]
    public string Country { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal? Budget { get; set; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    [RegularExpression(
        "^[A-Za-z]{3}$",
        ErrorMessage = "Currency code must be three letters.")]
    [Display(Name = "Currency")]
    public string CurrencyCode { get; set; } = "PKR";

    [DataType(DataType.Date)]
    [Display(Name = "Planned Start Date")]
    public DateTime? PlannedStartDateUtc { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Planned End Date")]
    public DateTime? PlannedEndDateUtc { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (PlannedStartDateUtc.HasValue &&
            PlannedEndDateUtc.HasValue &&
            PlannedEndDateUtc < PlannedStartDateUtc)
        {
            yield return new ValidationResult(
                "End date must be on or after start date.",
                new[] { nameof(PlannedEndDateUtc) });
        }
    }
}
