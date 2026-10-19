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

    public async Task<int> CreateAsync(CreateCompanyDto dto)
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

        return await _companyRepository.AddAsync(company);
    }
}