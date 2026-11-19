namespace ConstructionSaaS.Application.Interfaces;

public interface ITokenService
{
    string CreateToken(string userId, string email, IEnumerable<string> roles,
    int? companyId, string? securityStamp = null);
}
