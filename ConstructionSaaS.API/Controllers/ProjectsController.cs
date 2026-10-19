using ConstructionSaaS.Application.DTOs.Projects;
using ConstructionSaaS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;

    public ProjectsController(IProjectService projectService)
    {
        _projectService = projectService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProjectDto dto)
    {
        var projectId = await _projectService.CreateAsync(dto);

        return StatusCode(201, new
        {
            id = projectId,
            message = "Project created successfully"
        });
    }
}