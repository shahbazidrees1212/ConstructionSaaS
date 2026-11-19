using System.ComponentModel.DataAnnotations;
namespace ConstructionSaaS.Domain.Entities;
public class AccountProfile
{
    [Key, MaxLength(450)] public string UserId { get; set; } = "";
    [MaxLength(100)] public string FullName { get; set; } = "";
    [MaxLength(100)] public string? JobTitle { get; set; }
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(100)] public string? Location { get; set; }
    [MaxLength(500)] public string? Bio { get; set; }
    public byte[]? Photo { get; set; }
    [MaxLength(30)] public string? PhotoContentType { get; set; }
}
