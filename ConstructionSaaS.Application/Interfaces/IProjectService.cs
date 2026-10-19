using ConstructionSaaS.Application.DTOs.Projects;

namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectService
{
    Task<int> CreateAsync(CreateProjectDto dto);
}