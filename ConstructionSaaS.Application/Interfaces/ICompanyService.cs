using ConstructionSaaS.Application.DTOs.Companies;

namespace ConstructionSaaS.Application.Interfaces
{
    public interface ICompanyService
    {
        Task<int> CreateAsync(CreateCompanyDto dto);
    }
}
