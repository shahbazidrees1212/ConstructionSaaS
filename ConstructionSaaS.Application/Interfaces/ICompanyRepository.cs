using ConstructionSaaS.Domain.Entities;

namespace ConstructionSaaS.Application.Interfaces
{
    public interface ICompanyRepository
    {
        Task<int> AddAsync(Company company);
    }
}
