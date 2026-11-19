
using System.Security.Claims;
using ConstructionSaaS.Application.DTOs.Projects;
using ConstructionSaaS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;

    public ProjectsController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    private bool TryGetCompanyId(out int companyId)
    {
        var claimValue = User.FindFirst("company_id")?.Value;

        return int.TryParse(claimValue, out companyId)
               && companyId > 0;
    }

    private string? GetCurrentUserId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
    }

    // CREATE PROJECT - CompanyAdmin only
    [HttpPost]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> Create(
        [FromBody] CreateProjectDto dto)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var projectId = await _projectService
            .CreateAsync(dto, companyId);

        return StatusCode(201, new
        {
            id = projectId,
            message = "Project created successfully"
        });
    }

    // GET PROJECTS - Role-based filtering
    [HttpGet]
    [Authorize(Roles =
        "CompanyAdmin,ProjectManager,SiteEngineer")]
    public async Task<IActionResult> GetAll()
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        if (User.IsInRole("CompanyAdmin"))
        {
            var allProjects = await _projectService
                .GetAllAsync(companyId);

            return Ok(allProjects);
        }

        var userId = GetCurrentUserId();

        if (string.IsNullOrWhiteSpace(userId))
            return Forbid();

        var assignedProjects = await _projectService
            .GetAssignedProjectsAsync(companyId, userId);

        return Ok(assignedProjects);
    }

    // GET PROJECT BY ID - Role-based filtering
    [HttpGet("{id:int}")]
    [Authorize(Roles =
        "CompanyAdmin,ProjectManager,SiteEngineer")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        ProjectDto? project;

        if (User.IsInRole("CompanyAdmin"))
        {
            project = await _projectService
                .GetByIdAsync(id, companyId);
        }
        else
        {
            var userId = GetCurrentUserId();

            if (string.IsNullOrWhiteSpace(userId))
                return Forbid();

            project = await _projectService
                .GetAssignedProjectByIdAsync(
                    id, companyId, userId);
        }

        if (project == null)
            return NotFound();

        return Ok(project);
    }

    // UPDATE PROJECT - CompanyAdmin only
    [HttpPut("{id:int}")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateProjectDto dto)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var updated = await _projectService
            .UpdateAsync(id, dto, companyId);

        if (!updated)
        {
            return NotFound(new
            {
                message = "Project not found."
            });
        }

        return NoContent();
    }

    // DELETE PROJECT - CompanyAdmin only
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        bool deleted;
        try { deleted = await _projectService.DeleteAsync(id, companyId); }
        catch (ConstructionSaaS.Application.Interfaces.ProjectHasInvoicesException e)
        { return Conflict(new { message = e.Message }); }

        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
