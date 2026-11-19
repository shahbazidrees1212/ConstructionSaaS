namespace ConstructionSaaS.Application.Interfaces;
public interface IAuditActor
{
    string Name { get; }
    string? UserId { get; }
}
