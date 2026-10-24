namespace ConstructionSaaS.Application.DTOs.Auth;

public class LoginResultDto
{
    public bool Succeeded { get; set; }
    public string? Token { get; set; }
    public List<string> Errors { get; set; } = new();
}