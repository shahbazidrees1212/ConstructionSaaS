
using ConstructionSaaS.Application.DTOs.ProjectTasks;
using ConstructionSaaS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ConstructionSaaS.API.Controllers;

[ApiController]
[Route("api/project-tasks")]
[Authorize(Roles = "ProjectManager")]
public class ProjectTasksController : ControllerBase
{
    private readonly IProjectTaskService _taskService;

    public ProjectTasksController(
        IProjectTaskService taskService)
    {
        _taskService = taskService;
    }

    private string? CurrentUserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier);

    private int? CurrentCompanyId
    {
        get
        {
            var value = User.FindFirst("company_id")?.Value;

            return int.TryParse(value, out var id) && id > 0
                ? id
                : null;
        }
    }

    // GET api/project-tasks/project/1
    [HttpGet("project/{projectId:int}")]
    public async Task<IActionResult> GetByProject(
        int projectId)
    {
        if (CurrentCompanyId is not int companyId ||
            string.IsNullOrWhiteSpace(CurrentUserId))
            return Unauthorized();

        try
        {
            var tasks = await _taskService.GetByProjectIdAsync(
                companyId, CurrentUserId, projectId);

            return Ok(tasks);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) { return Conflict(new { message = "Task changed. Refresh and try again." }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    // GET api/project-tasks/5
    [HttpGet("{taskId:int}")]
    public async Task<IActionResult> GetById(int taskId)
    {
        if (CurrentCompanyId is not int companyId ||
            string.IsNullOrWhiteSpace(CurrentUserId))
            return Unauthorized();

        try
        {
            var task = await _taskService.GetByIdAsync(
                companyId, CurrentUserId, taskId);

            if (task == null)
                return NotFound();

            return Ok(task);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) { return Conflict(new { message = "Task changed. Refresh and try again." }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    // POST api/project-tasks
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProjectTaskDto dto)
    {
        if (CurrentCompanyId is not int companyId ||
            string.IsNullOrWhiteSpace(CurrentUserId))
            return Unauthorized();

        try
        {
            var task = await _taskService.CreateAsync(
                companyId, CurrentUserId, dto);

            return CreatedAtAction(
                nameof(GetById),
                new { taskId = task.Id },
                task);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) { return Conflict(new { message = "Task changed. Refresh and try again." }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // PUT api/project-tasks/5
    [HttpPut("{taskId:int}")]
    public async Task<IActionResult> Update(
        int taskId,
        [FromBody] UpdateProjectTaskDto dto)
    {
        if (CurrentCompanyId is not int companyId ||
            string.IsNullOrWhiteSpace(CurrentUserId))
            return Unauthorized();

        try
        {
            var updated = await _taskService.UpdateAsync(
                companyId, CurrentUserId, taskId, dto);

            if (!updated)
                return NotFound();

            return NoContent();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) { return Conflict(new { message = "Task changed. Refresh and try again." }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // DELETE api/project-tasks/5
    [HttpDelete("{taskId:int}")]
    public async Task<IActionResult> Delete(int taskId)
    {
        if (CurrentCompanyId is not int companyId ||
            string.IsNullOrWhiteSpace(CurrentUserId))
            return Unauthorized();

        try
        {
            var deleted = await _taskService.DeleteAsync(
                companyId, CurrentUserId, taskId);

            if (!deleted)
                return NotFound();

            return NoContent();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException) { return Conflict(new { message = "Task changed. Refresh and try again." }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
