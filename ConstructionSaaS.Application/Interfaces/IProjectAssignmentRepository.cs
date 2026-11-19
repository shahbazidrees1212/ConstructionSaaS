
using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectAssignmentRepository
{
    // Create new project assignment
    Task<int> AddAsync(ProjectAssignment assignment);

    // Get assignments for a project within a company
    Task<List<ProjectAssignment>> GetByProjectAsync(
        int projectId,
        int companyId);

    // Get one assignment within a company
    Task<ProjectAssignment?> GetByIdAsync(
        int assignmentId,
        int companyId);

    // Check whether an employee is already assigned
    Task<bool> ExistsAsync(
        int projectId,
        string userId,
        int companyId);

    // Remove project assignment
    Task DeleteAsync(ProjectAssignment assignment);
}
