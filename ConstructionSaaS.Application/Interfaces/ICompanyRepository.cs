using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Interfaces
{
    public interface ICompanyRepository
    {
        Task<int> AddAsync(Company company);
        Task<int> AddForUserAsync(Company company, string userId);
        Task<List<Company>> GetAllAsync(int companyId);
        Task<Company?> GetByIdAsync(int id, int companyId);
        Task UpdateAsync(Company company);
    }
}
