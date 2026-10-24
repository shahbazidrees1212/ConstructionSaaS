namespace ConstructionSaaS.Application.DTOs.Auth;

public class AuthResultDto
{
    public bool Succeeded { get; set; }

    public List<string> Errors { get; set; } = new();
}