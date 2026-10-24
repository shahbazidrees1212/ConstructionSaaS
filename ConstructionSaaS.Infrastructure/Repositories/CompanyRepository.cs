using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.Infrastructure.Repositories;

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

    public async Task<int> AddForUserAsync(Company company, string userId)
    {
        await using var transaction =
            await _context.Database.BeginTransactionAsync();

        var user = await _context.Users
            .SingleOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            throw new InvalidOperationException("User account was not found.");

        if (user.CompanyId.HasValue)
            throw new InvalidOperationException(
                "This user is already linked to a company.");

        _context.Companies.Add(company);
        await _context.SaveChangesAsync();

        user.CompanyId = company.Id;
        await _context.SaveChangesAsync();

        await transaction.CommitAsync();

        return company.Id;
    }

    public async Task<List<Company>> GetAllAsync(int companyId)
    {
        return await _context.Companies
            .AsNoTracking()
            .Where(c => c.Id == companyId && c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<Company?> GetByIdAsync(int id, int companyId)
    {
        return await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == id && c.Id == companyId);
    }

    public async Task UpdateAsync(Company company)
    {
        _context.Companies.Update(company);
        await _context.SaveChangesAsync();
    }
}