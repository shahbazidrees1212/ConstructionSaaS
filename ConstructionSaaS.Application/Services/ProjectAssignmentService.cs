
using ConstructionSaaS.Application.DTOs.ProjectAssignments;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Services;

public class ProjectAssignmentService : IProjectAssignmentService
{
    private readonly IProjectRepository _projectRepository;
    private readonly IProjectAssignmentRepository _assignmentRepository;
    private readonly IProjectAssignmentUserReader _userReader;

    public ProjectAssignmentService(
        IProjectRepository projectRepository,
        IProjectAssignmentRepository assignmentRepository,
        IProjectAssignmentUserReader userReader)
    {
        _projectRepository = projectRepository;
        _assignmentRepository = assignmentRepository;
        _userReader = userReader;
    }

    // ASSIGN EMPLOYEE TO PROJECT
    public async Task<AssignmentResult<int>> AssignAsync(
        AssignProjectMemberDto dto,
        int companyId)
    {
        if (companyId <= 0 ||
            dto.ProjectId <= 0 ||
            string.IsNullOrWhiteSpace(dto.UserId))
        {
            return AssignmentResult<int>.Fail(
                AssignmentError.InvalidInput);
        }

        // Project must belong to the current company
        var project = await _projectRepository.GetByIdAsync(
            dto.ProjectId, companyId);

        if (project == null)
        {
            return AssignmentResult<int>.Fail(
                AssignmentError.ProjectNotFound);
        }

        var user = await _userReader.GetByIdAsync(
            dto.UserId.Trim());

        if (user == null)
        {
            return AssignmentResult<int>.Fail(
                AssignmentError.UserNotFound);
        }

        // Prevent cross-company assignment
        if (user.CompanyId != companyId)
        {
            return AssignmentResult<int>.Fail(
                AssignmentError.WrongCompany);
        }

        if (!user.IsActive)
        {
            return AssignmentResult<int>.Fail(
                AssignmentError.InactiveUser);
        }

        // Only eligible team roles can be assigned
        bool allowedRole = user.Roles.Any(role =>
            role == "ProjectManager" ||
            role == "SiteEngineer");

        if (!allowedRole)
        {
            return AssignmentResult<int>.Fail(
                AssignmentError.InvalidRole);
        }

        bool alreadyAssigned =
            await _assignmentRepository.ExistsAsync(
                dto.ProjectId, user.Id, companyId);

        if (alreadyAssigned)
        {
            return AssignmentResult<int>.Fail(
                AssignmentError.AlreadyAssigned);
        }

        var assignment = new ProjectAssignment
        {
            ProjectId = dto.ProjectId,
            UserId = user.Id,
            AssignedAtUtc = DateTime.UtcNow
        };

        int assignmentId;
        try { assignmentId = await _assignmentRepository.AddAsync(assignment); }
        catch (DuplicateAssignmentException)
        {
            return AssignmentResult<int>.Fail(AssignmentError.AlreadyAssigned);
        }

        return AssignmentResult<int>.Ok(assignmentId);
    }

    // GET ASSIGNED EMPLOYEES
    public async Task<AssignmentResult<List<ProjectAssignmentDto>>>
        GetByProjectAsync(int projectId, int companyId)
    {
        if (projectId <= 0 || companyId <= 0)
        {
            return AssignmentResult<List<ProjectAssignmentDto>>.Fail(
                AssignmentError.InvalidInput);
        }

        var project = await _projectRepository.GetByIdAsync(
            projectId, companyId);

        if (project == null)
        {
            return AssignmentResult<List<ProjectAssignmentDto>>.Fail(
                AssignmentError.ProjectNotFound);
        }

        var assignments =
            await _assignmentRepository.GetByProjectAsync(
                projectId, companyId);

        var result = new List<ProjectAssignmentDto>();
        foreach (var assignment in assignments)
        {
            var user = await _userReader.GetByIdAsync(assignment.UserId);
            // Never expose details from a different tenant, including stale assignments.
            if (user?.CompanyId != companyId) user = null;
            var eligible = user?.Roles.Any(r => r == "ProjectManager" || r == "SiteEngineer") == true;
            result.Add(new ProjectAssignmentDto
            {
                Id = assignment.Id,
                ProjectId = assignment.ProjectId,
                UserId = assignment.UserId,
                AssignedAtUtc = assignment.AssignedAtUtc,
                Email = user?.Email ?? string.Empty,
                Role = user == null ? string.Empty : string.Join(", ", user.Roles),
                IsActive = user?.IsActive == true,
                RequiresReview = user == null || !user.IsActive || !eligible
            });
        }
        return AssignmentResult<List<ProjectAssignmentDto>>.Ok(result);
    }

    // REMOVE ASSIGNMENT
    public async Task<AssignmentResult<bool>> RemoveAsync(
        int assignmentId, int companyId)
    {
        if (assignmentId <= 0 || companyId <= 0)
        {
            return AssignmentResult<bool>.Fail(
                AssignmentError.InvalidInput);
        }

        var assignment =
            await _assignmentRepository.GetByIdAsync(
                assignmentId, companyId);

        if (assignment == null)
        {
            return AssignmentResult<bool>.Fail(
                AssignmentError.AssignmentNotFound);
        }

        await _assignmentRepository.DeleteAsync(assignment);

        return AssignmentResult<bool>.Ok(true);
    }
}

