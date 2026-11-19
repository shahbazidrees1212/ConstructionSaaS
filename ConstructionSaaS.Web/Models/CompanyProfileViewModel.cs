using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Web.Models;

public class CompanyProfileViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Company name is required.")]
    [StringLength(200, ErrorMessage = "Company name cannot exceed 200 characters.")]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}
