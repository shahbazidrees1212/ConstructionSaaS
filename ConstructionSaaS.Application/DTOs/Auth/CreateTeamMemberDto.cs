using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Application.DTOs.Auth;

public class CreateTeamMemberDto
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string Role { get; set; } = string.Empty;
}