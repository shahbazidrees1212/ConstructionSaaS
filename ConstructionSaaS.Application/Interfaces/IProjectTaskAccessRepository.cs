namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectTaskAccessRepository
{
    Task<bool> CanManageProjectAsync(
        int companyId,
        string userId,
        int projectId);

    Task<bool> IsValidSiteEngineerAsync(
        int companyId,
        string userId);
}