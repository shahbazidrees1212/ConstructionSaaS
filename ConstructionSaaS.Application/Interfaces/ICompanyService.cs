using ConstructionSaaS.Application.DTOs.Companies;

namespace ConstructionSaaS.Application.Interfaces
{
    public interface ICompanyService
    {
        Task<int> CreateAsync(CreateCompanyDto dto, string userId);
        Task<List<CompanyDto>> GetAllAsync(int companyId);
        Task<CompanyDto?> GetByIdAsync(int id, int companyId);
        Task<bool> UpdateAsync(int id, UpdateCompanyDto dto, int companyId);
        Task<bool> DeactivateAsync(int id, int companyId);
    }
}
