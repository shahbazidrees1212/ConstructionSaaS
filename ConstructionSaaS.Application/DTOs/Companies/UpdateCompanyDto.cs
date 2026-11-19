using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Application.DTOs.Companies;

public class UpdateCompanyDto
{
    [Required(ErrorMessage = "Company name is required.")]
    [StringLength(200, ErrorMessage = "Company name cannot exceed 200 characters.")]
    public string Name { get; set; } = string.Empty;
}