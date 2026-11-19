
using ConstructionSaaS.Application.DTOs.ProjectAssignments;
using ConstructionSaaS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "CompanyAdmin")]
public class ProjectAssignmentsController : ControllerBase
{
    private readonly IProjectAssignmentService _assignmentService;

    public ProjectAssignmentsController(
        IProjectAssignmentService assignmentService)
    {
        _assignmentService = assignmentService;
    }

    private bool TryGetCompanyId(out int companyId)
    {
        var claimValue = User.FindFirst("company_id")?.Value;

        return int.TryParse(claimValue, out companyId)
               && companyId > 0;
    }

    // =====================================
    // ASSIGN EMPLOYEE TO PROJECT
    // =====================================

    [HttpPost]
    public async Task<IActionResult> Assign(
        [FromBody] AssignProjectMemberDto dto)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var result = await _assignmentService.AssignAsync(
            dto, companyId);

        if (!result.Success)
            return MapError(result.Error);

        return StatusCode(201, new
        {
            id = result.Data,
            message = "Employee assigned to project successfully."
        });
    }

    // =====================================
    // GET PROJECT ASSIGNMENTS
    // =====================================

    [HttpGet("project/{projectId:int}")]
    public async Task<IActionResult> GetByProject(int projectId)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var result = await _assignmentService.GetByProjectAsync(
            projectId, companyId);

        if (!result.Success)
            return MapError(result.Error);

        return Ok(result.Data);
    }

    // =====================================
    // REMOVE PROJECT ASSIGNMENT
    // =====================================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var result = await _assignmentService.RemoveAsync(
            id, companyId);

        if (!result.Success)
            return MapError(result.Error);

        return NoContent();
    }

    // =====================================
    // MAP BUSINESS ERRORS TO HTTP STATUS
    // =====================================

    private IActionResult MapError(AssignmentError error)
    {
        return error switch
        {
            AssignmentError.InvalidInput =>
                BadRequest(new
                {
                    message = "Invalid assignment information."
                }),

            AssignmentError.ProjectNotFound =>
                NotFound(new
                {
                    message = "Project not found."
                }),

            AssignmentError.UserNotFound =>
                NotFound(new
                {
                    message = "Employee not found."
                }),

            AssignmentError.WrongCompany =>
                NotFound(new
                {
                    message = "Employee not found."
                }),

            AssignmentError.InactiveUser =>
                BadRequest(new
                {
                    message = "Cannot assign an inactive employee."
                }),

            AssignmentError.InvalidRole =>
                BadRequest(new
                {
                    message = "Only ProjectManager or SiteEngineer can be assigned."
                }),

            AssignmentError.AlreadyAssigned =>
                Conflict(new
                {
                    message = "Employee is already assigned to this project."
                }),

            AssignmentError.AssignmentNotFound =>
                NotFound(new
                {
                    message = "Project assignment not found."
                }),

            _ => StatusCode(500, new
            {
                message = "Unexpected assignment error."
            })
        };
    }
}
