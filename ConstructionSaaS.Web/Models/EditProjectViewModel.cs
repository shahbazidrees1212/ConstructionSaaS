
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Web.Models;

public class EditProjectViewModel : IValidatableObject
{
    public int Id { get; set; }

    // Basic Information
    [Required]
    [StringLength(50)]
    public string ProjectCode { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    // Project Location
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

    // Budget Information
    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal? Budget { get; set; }

    [Required]
    [StringLength(3, MinimumLength = 3)]
    [RegularExpression(
        "^[A-Za-z]{3}$",
        ErrorMessage = "Currency code must be three letters.")]
    public string CurrencyCode { get; set; } = string.Empty;

    // Planned Timeline
    [DataType(DataType.Date)]
    public DateTime? PlannedStartDateUtc { get; set; }

    [DataType(DataType.Date)]
    public DateTime? PlannedEndDateUtc { get; set; }

    // Project Status
    [Required(ErrorMessage = "Please select a project status.")]
    [RegularExpression(
        "^(Planning|Active|OnHold|Completed|Cancelled)$",
        ErrorMessage = "Please select a valid project status.")]
    public string Status { get; set; } = "Planning";

    // Custom Validation
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
