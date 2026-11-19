using ConstructionSaaS.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace ConstructionSaaS.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    public bool IsActive { get; set; } = true;
}