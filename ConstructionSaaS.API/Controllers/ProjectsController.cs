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
        return int.TryParse(claimValue, out companyId);
    }

    [HttpPost]
    [Authorize(Roles = "CompanyAdmin,ProjectManager")]
    public async Task<IActionResult> Create([FromBody] CreateProjectDto dto)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var projectId = await _projectService.CreateAsync(dto, companyId);

        return StatusCode(201, new
        {
            id = projectId,
            message = "Project created successfully"
        });
    }

    [HttpGet]
    [Authorize(Roles = "CompanyAdmin,ProjectManager,SiteEngineer")]
    public async Task<IActionResult> GetAll()
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var projects = await _projectService.GetAllAsync(companyId);
        return Ok(projects);
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = "CompanyAdmin,ProjectManager,SiteEngineer")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var project = await _projectService.GetByIdAsync(id, companyId);

        if (project == null)
            return NotFound();

        return Ok(project);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "CompanyAdmin,ProjectManager")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateProjectDto dto)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var updated = await _projectService.UpdateAsync(id, dto, companyId);

        if (!updated)
            return NotFound(new { message = "Project nahi mila." });

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var deleted = await _projectService.DeleteAsync(id, companyId);

        if (!deleted)
            return NotFound();

        return NoContent();
    }
}