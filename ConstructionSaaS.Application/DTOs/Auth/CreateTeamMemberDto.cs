using System.ComponentModel.DataAnnotations;

namespace ConstructionSaaS.Application.DTOs.Auth;

public class CreateTeamMemberDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = string.Empty;
}