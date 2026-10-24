using ConstructionSaaS.Application.DTOs.Projects;

namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectService
{
    Task<int> CreateAsync(CreateProjectDto dto, int companyId);
    Task<List<ProjectDto>> GetAllAsync(int companyId);
    Task<ProjectDto?> GetByIdAsync(int id, int companyId);
    Task<bool> UpdateAsync(int id, UpdateProjectDto dto, int companyId);
    Task<bool> DeleteAsync(int id, int companyId);
}