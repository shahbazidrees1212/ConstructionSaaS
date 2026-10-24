using ConstructionSaaS.Application.DTOs.Auth;

namespace ConstructionSaaS.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResultDto> RegisterAsync(RegisterRequestDto dto);
    Task<LoginResultDto> LoginAsync(LoginRequestDto dto);
    Task<AuthResultDto> CreateTeamMemberAsync(
       CreateTeamMemberDto dto,
       int companyId);
}