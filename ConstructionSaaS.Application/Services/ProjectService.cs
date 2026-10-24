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

    public async Task<int> CreateAsync(CreateProjectDto dto, int companyId)
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

    public async Task<bool> DeleteAsync(int id, int companyId)
    {
        var project = await _projectRepository.GetByIdAsync(id, companyId);

        if (project == null)
            return false;

        await _projectRepository.DeleteAsync(project);
        return true;
    }

    public async Task<List<ProjectDto>> GetAllAsync(int companyId)
    {
        var projects = await _projectRepository.GetAllAsync(companyId);

        return projects.Select(p => new ProjectDto
        {
            Id = p.Id,
            CompanyId = p.CompanyId,
            ProjectCode = p.ProjectCode,
            Name = p.Name,
            Country = p.Country,
            Budget = p.Budget,
            CurrencyCode = p.CurrencyCode,
            PlannedStartDateUtc = p.PlannedStartDateUtc,
            PlannedEndDateUtc = p.PlannedEndDateUtc
        }).ToList();
    }

    public async Task<ProjectDto?> GetByIdAsync(int id, int companyId)
    {
        var project = await _projectRepository.GetByIdAsync(id, companyId);

        if (project is null)
            return null;

        return new ProjectDto
        {
            Id = project.Id,
            CompanyId = project.CompanyId,
            ProjectCode = project.ProjectCode,
            Name = project.Name,
            Country = project.Country,
            Budget = project.Budget,
            CurrencyCode = project.CurrencyCode,
            PlannedStartDateUtc = project.PlannedStartDateUtc,
            PlannedEndDateUtc = project.PlannedEndDateUtc
        };
    }

    public async Task<bool> UpdateAsync(
        int id,
        UpdateProjectDto dto,
        int companyId)
    {
        var project = await _projectRepository.GetByIdAsync(id, companyId);

        if (project is null)
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
        project.CurrencyCode = dto.CurrencyCode.Trim().ToUpperInvariant();
        project.PlannedStartDateUtc = dto.PlannedStartDateUtc;
        project.PlannedEndDateUtc = dto.PlannedEndDateUtc;
        project.UpdatedAtUtc = DateTime.UtcNow;

        await _projectRepository.UpdateAsync(project);
        return true;
    }
}