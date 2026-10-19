using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
namespace ConstructionSaaS.Infrastructure.Repositories
{
    public class CompanyRepository : ICompanyRepository
    {
        private readonly ApplicationDbContext _context;

        public CompanyRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<int> AddAsync(Company company)
        {
            _context.Companies.Add(company);
            await _context.SaveChangesAsync();

            return company.Id;
        }
    }

}