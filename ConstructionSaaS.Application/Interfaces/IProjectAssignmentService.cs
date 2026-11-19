
using ConstructionSaaS.Application.DTOs.ProjectAssignments;

namespace ConstructionSaaS.Application.Interfaces;

public interface IProjectAssignmentService
{
    Task<AssignmentResult<int>> AssignAsync(
        AssignProjectMemberDto dto,
        int companyId);

    Task<AssignmentResult<List<ProjectAssignmentDto>>>
        GetByProjectAsync(int projectId, int companyId);

    Task<AssignmentResult<bool>> RemoveAsync(
        int assignmentId,
        int companyId);
}
