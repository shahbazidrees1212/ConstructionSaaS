using System.Security.Claims;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ConstructionSaaS.Infrastructure.Persistence;
public partial class ApplicationDbContext
{
    private readonly IAuditActor? _auditActor;
    private readonly NotificationSignal? _notificationSignal;
    public string AuditActor => _auditActor?.Name ?? "System";

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        var audit = new List<WorkspaceActivity>();
        var notices = new List<WorkspaceNotification>();
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList())
        {
            int? company = null;
            string category = "", label = "";
            int? projectId = null;
            switch (entry.Entity)
            {
                case SiteWorkEntry work: projectId = work.ProjectId; category = "Work updates"; label = work.Kind + ": " + work.Summary; break;
                case ProjectTask task: company = task.CompanyId; projectId = task.ProjectId; category = "Tasks"; label = task.Title; break;
                case Project p: projectId = p.Id > 0 ? p.Id : null; company = p.CompanyId; category = "Projects"; label = p.Name; break;
                case Company c when entry.State != EntityState.Added: company = c.Id; category = "Company"; label = c.Name; break;
                case ApplicationUser u when u.CompanyId.HasValue: company = u.CompanyId; category = "Team"; label = u.Email ?? "Account"; break;
                case ProjectAssignment a: projectId = a.ProjectId; category = "Assignments"; label = "Project team assignment"; break;
                case ProjectMilestone m: projectId = m.ProjectId; category = "Milestones"; label = m.Title; break;
                case ProjectDocument d: projectId = d.ProjectId; category = "Documents"; label = d.FileName; break;
            }
            if (projectId.HasValue)
                company = await Projects.Where(p => p.Id == projectId).Select(p => (int?)p.CompanyId).FirstOrDefaultAsync(cancellationToken);
            if (company is > 0)
            {
                var verb = entry.State == EntityState.Added ? "Created" : entry.State == EntityState.Deleted ? "Removed" : "Updated";
                var description = $"{verb}: {label}";
                if (entry.Entity is ProjectTask taskChange) {
                    var originalStatus = entry.State == EntityState.Modified ? entry.OriginalValues.GetValue<string>("Status") : "";
                    description += $" [status: {originalStatus} -> {taskChange.Status}; priority: {taskChange.Priority}; due: {taskChange.DueDateUtc:yyyy-MM-dd}]";
                    if (!string.IsNullOrEmpty(taskChange.AssignedToUserId)) {
                        var assigneeEmail = await Users.Where(u => u.Id == taskChange.AssignedToUserId && u.CompanyId == company).Select(u => u.Email).FirstOrDefaultAsync(cancellationToken);
                        description += $" Assigned to: {assigneeEmail ?? "Unavailable"}";
                    }
                    if (entry.State != EntityState.Deleted) {
                        var recipients = await ProjectAssignments.Where(a => a.ProjectId == taskChange.ProjectId && (a.UserId == taskChange.AssignedToUserId || UserRoles.Any(ur => ur.UserId == a.UserId && Roles.Any(r => r.Id == ur.RoleId && r.Name == "ProjectManager")))).Select(a => a.UserId).ToListAsync(cancellationToken);
                        foreach (var recipient in recipients.Distinct())
                            notices.Add(new WorkspaceNotification { CompanyId = company.Value, ProjectId = taskChange.ProjectId, RecipientUserId = recipient,
                                Message = (recipient == taskChange.AssignedToUserId ? "Your task: " : "Task update: ") + taskChange.Title + $" — {taskChange.Status}; due {taskChange.DueDateUtc:dd MMM yyyy}" });
                    }
                }
                if (entry.Entity is SiteWorkEntry workUpdate && entry.State == EntityState.Added) {
                    var recipients = await ProjectAssignments.Where(a => a.ProjectId == workUpdate.ProjectId &&
                        UserRoles.Any(ur => ur.UserId == a.UserId && Roles.Any(r => r.Id == ur.RoleId && r.Name == "ProjectManager"))).Select(a => a.UserId).ToListAsync(cancellationToken);
                    foreach (var recipient in recipients.Distinct())
                        notices.Add(new WorkspaceNotification { CompanyId = company.Value, ProjectId = workUpdate.ProjectId, RecipientUserId = recipient, Message = $"{workUpdate.Kind} by {workUpdate.AuthorEmail}" });
                }
                if (entry.Entity is ProjectMilestone milestone && entry.State != EntityState.Deleted) {
                    var recipients = await ProjectAssignments.Where(a => a.ProjectId == milestone.ProjectId).Select(a => a.UserId).ToListAsync(cancellationToken);
                    foreach (var recipient in recipients.Distinct())
                        notices.Add(new WorkspaceNotification { CompanyId = company.Value, ProjectId = milestone.ProjectId, RecipientUserId = recipient,
                            Message = $"Milestone {verb.ToLowerInvariant()}: {milestone.Title} — {milestone.Progress}%; due {milestone.DueDateUtc:dd MMM yyyy}" });
                }
                if (projectId is > 0 && entry.Entity is not Company && entry.Entity is not ApplicationUser)
                {
                    var adminIds = await Users.Where(u => u.CompanyId == company && u.IsActive &&
                        UserRoles.Any(ur => ur.UserId == u.Id && Roles.Any(r => r.Id == ur.RoleId && r.Name == "CompanyAdmin")))
                        .Select(u => u.Id).ToListAsync(cancellationToken);
                    foreach (var recipient in adminIds)
                        if (!notices.Any(n => n.ProjectId == projectId && n.RecipientUserId == recipient))
                            notices.Add(new WorkspaceNotification { CompanyId = company.Value, ProjectId = projectId.Value,
                                RecipientUserId = recipient, Message = (category + ": " + description)[..Math.Min((category + ": " + description).Length, 500)] });
                }
                audit.Add(new WorkspaceActivity { CompanyId = company.Value, ProjectId = projectId, Category = category, Description = description[..Math.Min(description.Length, 500)], Actor = AuditActor });
            }
        }
        WorkspaceActivities.AddRange(audit);
        // Keep audit history, but do not notify the authenticated actor of their own action.
        var actorId = _auditActor?.UserId;
        if (!string.IsNullOrEmpty(actorId))
            notices.RemoveAll(n => n.RecipientUserId == actorId);
        WorkspaceNotifications.AddRange(notices);
        var recipientsChanged = ChangeTracker.Entries<WorkspaceNotification>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => (e.Entity.CompanyId, e.Entity.RecipientUserId)).Distinct().ToList();
        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            foreach (var recipient in recipientsChanged)
                _notificationSignal?.AfterSave(Database.CurrentTransaction?.GetDbTransaction(), recipient.CompanyId, recipient.RecipientUserId);
            return result;
        }
        catch
        {
            foreach (var item in audit) Entry(item).State = EntityState.Detached;
            foreach (var item in notices) Entry(item).State = EntityState.Detached;
            throw;
        }
    }
}

