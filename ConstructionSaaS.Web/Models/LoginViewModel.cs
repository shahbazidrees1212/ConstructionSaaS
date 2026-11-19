using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Web.Models;

public class LoginViewModel
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}