using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectRepository
{
    Task<int> AddAsync(Project project);
}