namespace ConstructionSaaS.Application.DTOs.Auth;

public class TeamMemberDto
{
    public string Id { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public int CompanyId { get; set; }

    public bool IsActive { get; set; }
}