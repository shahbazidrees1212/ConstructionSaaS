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

    public async Task<int> CreateAsync(CreateProjectDto dto)
    {
        var project = new Project
        {
            CompanyId = dto.CompanyId,
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
}