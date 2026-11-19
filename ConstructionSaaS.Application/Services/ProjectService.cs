
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConstructionSaaS.Application.DTOs.Projects;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Services;

public class ProjectService : IProjectService
{
    private readonly IProjectRepository _projectRepository;

    public ProjectService(IProjectRepository projectRepository)
    {
        _projectRepository = projectRepository;
    }

    // CREATE PROJECT
    public async Task<int> CreateAsync(
        CreateProjectDto dto, int companyId)
    {
        var project = new Project
        {
            CompanyId = companyId,
            ProjectCode = dto.ProjectCode.Trim(),
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim(),
            AddressLine1 = dto.AddressLine1?.Trim(),
            City = dto.City?.Trim(),
            StateOrProvince = dto.StateOrProvince?.Trim(),
            PostalCode = dto.PostalCode?.Trim(),
            Country = dto.Country.Trim(),
            Budget = dto.Budget,
            CurrencyCode = dto.CurrencyCode.Trim().ToUpperInvariant(),
            PlannedStartDateUtc = dto.PlannedStartDateUtc,
            PlannedEndDateUtc = dto.PlannedEndDateUtc
        };

        return await _projectRepository.AddAsync(project);
    }

    // GET ALL PROJECTS
    public async Task<List<ProjectDto>> GetAllAsync(int companyId)
    {
        var projects = await _projectRepository
            .GetAllAsync(companyId);

        return projects.Select(MapToDto).ToList();
    }

    // GET PROJECT BY ID
    public async Task<ProjectDto?> GetByIdAsync(
        int id, int companyId)
    {
        var project = await _projectRepository
            .GetByIdAsync(id, companyId);

        return project == null ? null : MapToDto(project);
    }

    // UPDATE PROJECT
    public async Task<bool> UpdateAsync(
        int id, UpdateProjectDto dto, int companyId)
    {
        var project = await _projectRepository
            .GetByIdAsync(id, companyId);

        if (project == null)
            return false;

        project.ProjectCode = dto.ProjectCode.Trim();
        project.Name = dto.Name.Trim();
        project.Description = dto.Description?.Trim();
        project.AddressLine1 = dto.AddressLine1?.Trim();
        project.City = dto.City?.Trim();
        project.StateOrProvince = dto.StateOrProvince?.Trim();
        project.PostalCode = dto.PostalCode?.Trim();
        project.Country = dto.Country.Trim();
        project.Budget = dto.Budget;
        project.CurrencyCode =
            dto.CurrencyCode.Trim().ToUpperInvariant();

        project.PlannedStartDateUtc =
            dto.PlannedStartDateUtc;

        project.PlannedEndDateUtc =
            dto.PlannedEndDateUtc;

        // Update project status
        project.Status = dto.Status;

        project.UpdatedAtUtc = DateTime.UtcNow;

        await _projectRepository.UpdateAsync(project);

        return true;
    }

    // DELETE PROJECT
    public async Task<bool> DeleteAsync(
        int id, int companyId)
    {
        var project = await _projectRepository
            .GetByIdAsync(id, companyId);

        if (project == null)
            return false;

        await _projectRepository.DeleteAsync(project);

        return true;
    }

    // Get projects assigned to the logged-in user
    public async Task<List<ProjectDto>> GetAssignedProjectsAsync(
        int companyId,
        string userId)
    {
        if (companyId <= 0 ||
            string.IsNullOrWhiteSpace(userId))
        {
            return new List<ProjectDto>();
        }

        var projects = await _projectRepository
            .GetAssignedProjectsAsync(companyId, userId);

        return projects.Select(MapToDto).ToList();
    }

    // Get one project only if assigned to this user
    public async Task<ProjectDto?> GetAssignedProjectByIdAsync(
        int projectId,
        int companyId,
        string userId)
    {
        if (projectId <= 0 ||
            companyId <= 0 ||
            string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var project = await _projectRepository
            .GetAssignedProjectByIdAsync(
                projectId,
                companyId,
                userId);

        return project == null
            ? null
            : MapToDto(project);
    }


    // PROJECT ENTITY -> PROJECT DTO
    private static ProjectDto MapToDto(Project project)
    {
        return new ProjectDto
        {
            Id = project.Id,
            CompanyId = project.CompanyId,

            ProjectCode = project.ProjectCode,
            Name = project.Name,
            Description = project.Description,

            AddressLine1 = project.AddressLine1,
            City = project.City,
            StateOrProvince = project.StateOrProvince,
            PostalCode = project.PostalCode,
            Country = project.Country,

            Budget = project.Budget,
            CurrencyCode = project.CurrencyCode,

            PlannedStartDateUtc = project.PlannedStartDateUtc,
            PlannedEndDateUtc = project.PlannedEndDateUtc,

            ActualStartDateUtc = project.ActualStartDateUtc,
            ActualEndDateUtc = project.ActualEndDateUtc,

            Status = project.Status.ToString(),

            CreatedAtUtc = project.CreatedAtUtc,
            UpdatedAtUtc = project.UpdatedAtUtc
        };
    }
}
