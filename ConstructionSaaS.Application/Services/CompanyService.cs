using ConstructionSaaS.Application.DTOs.Companies;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Services;

public class CompanyService : ICompanyService
{
    private readonly ICompanyRepository _companyRepository;

    public CompanyService(ICompanyRepository companyRepository)
    {
        _companyRepository = companyRepository;
    }

    public async Task<int> CreateAsync(CreateCompanyDto dto, string userId)
    {
        var company = new Company
        {
            Name = dto.Name.Trim(),
            LegalName = dto.LegalName?.Trim(),
            RegistrationNumber = dto.RegistrationNumber?.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            PhoneNumber = dto.PhoneNumber?.Trim(),
            Website = dto.Website?.Trim(),
            AddressLine1 = dto.AddressLine1?.Trim(),
            AddressLine2 = dto.AddressLine2?.Trim(),
            City = dto.City?.Trim(),
            StateOrProvince = dto.StateOrProvince?.Trim(),
            PostalCode = dto.PostalCode?.Trim(),
            Country = dto.Country.Trim()
        };

        return await _companyRepository.AddForUserAsync(company, userId);
    }

    public async Task<List<CompanyDto>> GetAllAsync(int companyId)
    {
        var companies = await _companyRepository.GetAllAsync(companyId);

        return companies.Select(c => new CompanyDto
        {
            Id = c.Id,
            Name = c.Name,
            IsActive = c.IsActive
        }).ToList();
    }

    public async Task<CompanyDto?> GetByIdAsync(int id, int companyId)
    {
        var company = await _companyRepository.GetByIdAsync(id, companyId);

        if (company == null)
            return null;

        return new CompanyDto
        {
            Id = company.Id,
            Name = company.Name,
            IsActive = company.IsActive
        };
    }

    public async Task<bool> UpdateAsync(
        int id,
        UpdateCompanyDto dto,
        int companyId)
    {
        var company = await _companyRepository.GetByIdAsync(id, companyId);

        if (company == null)
            return false;

        company.Name = dto.Name.Trim();
        company.UpdatedAtUtc = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company);
        return true;
    }

    public async Task<bool> DeactivateAsync(int id, int companyId)
    {
        var company = await _companyRepository.GetByIdAsync(id, companyId);

        if (company == null)
            return false;

        company.IsActive = false;
        company.UpdatedAtUtc = DateTime.UtcNow;

        await _companyRepository.UpdateAsync(company);
        return true;
    }
}