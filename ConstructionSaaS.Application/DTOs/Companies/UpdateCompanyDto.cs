using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Application.DTOs.Companies;

public class UpdateCompanyDto
{
    [Required(ErrorMessage = "Company name is required.")]
    public string Name { get; set; } = string.Empty;
}