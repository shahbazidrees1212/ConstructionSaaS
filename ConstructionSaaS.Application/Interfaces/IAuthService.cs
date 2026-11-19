using ConstructionSaaS.Application.DTOs.Auth;

namespace ConstructionSaaS.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResultDto> RegisterAsync(RegisterRequestDto dto);

    Task<LoginResultDto> LoginAsync(LoginRequestDto dto);

    Task<AuthResultDto> CreateTeamMemberAsync(
        CreateTeamMemberDto dto,
        int companyId);

    // CompanyAdmin: List team members
    Task<List<TeamMemberDto>> GetTeamMembersAsync(
        int companyId);

    // CompanyAdmin: Update team member
    Task<AuthResultDto> UpdateTeamMemberAsync(
        string userId,
        UpdateTeamMemberDto dto,
        int companyId);

    // CompanyAdmin: Deactivate team member
    Task<AuthResultDto> DeactivateTeamMemberAsync(
        string userId,
        int companyId);
}