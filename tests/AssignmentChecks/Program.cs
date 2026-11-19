using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Security.Claims;
using ConstructionSaaS.Application.DTOs.Auth;
using ConstructionSaaS.Application.DTOs.ProjectAssignments;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Application.Services;
using ConstructionSaaS.Domain.Entities;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using ConstructionSaaS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ApiController = ConstructionSaaS.API.Controllers.ProjectAssignmentsController;

// Only a new, uniquely named local test database is created/deleted. No app config is read.
if (args.Contains("--subscription-only")) {
    await SubscriptionChecks.RunAsync((ok, name) => {
        if (!ok) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    });
    return;
}
if (args.Contains("--payment-provider-only")) {
    await PaymentProviderChecks.RunAsync((ok, name) => {
        if (!ok) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    });
    return;
}
var databaseName = "ConstructionSaaS_AssignmentChecks_" + Guid.NewGuid().ToString("N");
var connection = $@"Server=(localdb)\MSSQLLocalDB;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=20";
var services = new ServiceCollection();
services.AddLogging();
services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
var reader = new ProjectAssignmentUserReader(users);
ProjectAssignmentService Service(ApplicationDbContext context, IProjectAssignmentRepository? repository = null) =>
    new(new ProjectRepository(context), repository ?? new ProjectAssignmentRepository(context), reader);
var service = Service(db);
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
try
{
    await db.GetService<IMigrator>().MigrateAsync("20261102054054_AddProjectAssignments");
    await db.Database.ExecuteSqlRawAsync("INSERT INTO Companies (Name, Email, Country, CreatedAtUtc, IsActive) VALUES ('Migration preservation check', 'migration@example.test', 'PK', SYSUTCDATETIME(), 1)");
    await db.Database.MigrateAsync();
    Check(await db.Companies.AnyAsync(c => c.Email == "migration@example.test"), "Workspace migration preserves existing company records");
    foreach (var name in new[] { "ProjectManager", "SiteEngineer", "CompanyAdmin" })
        Check((await roles.CreateAsync(new IdentityRole(name))).Succeeded, "Seed role " + name);
    var firstCompany = new Company { Name = "Test A", Email = "a@example.test", Country = "PK" };
    var otherCompany = new Company { Name = "Test B", Email = "b@example.test", Country = "PK" };
    db.Companies.AddRange(firstCompany, otherCompany); await db.SaveChangesAsync();
    async Task<ApplicationUser> User(string email, int company, string role, bool active = true)
    {
        var user = new ApplicationUser { UserName = email, Email = email, CompanyId = company, IsActive = active };
        Check((await users.CreateAsync(user)).Succeeded, "Seed " + email);
        Check((await users.AddToRoleAsync(user, role)).Succeeded, "Seed user role");
        return user;
    }
    var employee = await User("employee@example.test", firstCompany.Id, "SiteEngineer");
    var outsider = await User("outsider@example.test", otherCompany.Id, "SiteEngineer");
    var inactive = await User("inactive@example.test", firstCompany.Id, "SiteEngineer", false);
    var admin = await User("admin@example.test", firstCompany.Id, "CompanyAdmin");
    var project = new Project { CompanyId = firstCompany.Id, ProjectCode = "TEST", Name = "Test project", Country = "PK", CurrencyCode = "PKR" };
    db.Projects.Add(project); await db.SaveChangesAsync();
    AssignProjectMemberDto Dto(string user) => new() { ProjectId = project.Id, UserId = user };
    Check((await service.AssignAsync(Dto(employee.Id), otherCompany.Id)).Error == AssignmentError.ProjectNotFound, "Reject cross-company project");
    Check((await service.AssignAsync(Dto(outsider.Id), firstCompany.Id)).Error == AssignmentError.WrongCompany, "Reject cross-company employee");
    Check((await service.AssignAsync(Dto(inactive.Id), firstCompany.Id)).Error == AssignmentError.InactiveUser, "Reject inactive employee");
    Check((await service.AssignAsync(Dto(admin.Id), firstCompany.Id)).Error == AssignmentError.InvalidRole, "Reject admin assignment");
    Check((await service.AssignAsync(Dto("missing"), firstCompany.Id)).Error == AssignmentError.UserNotFound, "Reject unknown employee");
    Check((await service.AssignAsync(new(), firstCompany.Id)).Error == AssignmentError.InvalidInput, "Reject invalid input");
    var assigned = await service.AssignAsync(Dto(employee.Id), firstCompany.Id);
    Check(assigned.Success, "Assign eligible employee");
    Check((await service.AssignAsync(Dto(employee.Id), firstCompany.Id)).Error == AssignmentError.AlreadyAssigned, "Reject duplicate assignment");
    Check((await service.GetByProjectAsync(project.Id, otherCompany.Id)).Error == AssignmentError.ProjectNotFound, "Deny foreign assignment list");
    Check((await service.RemoveAsync(assigned.Data, otherCompany.Id)).Error == AssignmentError.AssignmentNotFound, "Deny foreign assignment removal");
    employee.IsActive = false; await users.UpdateAsync(employee);
    var details = (await service.GetByProjectAsync(project.Id, firstCompany.Id)).Data!.Single();
    Check(details.Email == employee.Email && details.Role == "SiteEngineer" && !details.IsActive && details.RequiresReview, "Inactive assignment retains email and role with review flag");
    employee.IsActive = true; await users.UpdateAsync(employee);
    var auth = new AuthService(users, null!, roles, db);
    var blocked = await auth.UpdateTeamMemberAsync(employee.Id, new UpdateTeamMemberDto { Email = employee.Email!, Role = "ProjectManager" }, firstCompany.Id);
    Check(!blocked.Succeeded && blocked.Errors.Any(e => e.Contains("assignments")), "Role change requires assignment review");
    Check((await users.GetRolesAsync((await users.FindByIdAsync(employee.Id))!)).Contains("SiteEngineer"), "Rejected role change preserves previous role");
    Check((await service.RemoveAsync(assigned.Data, firstCompany.Id)).Success, "Remove assignment");
    Check((await service.RemoveAsync(assigned.Data, firstCompany.Id)).Error == AssignmentError.AssignmentNotFound, "Repeated remove returns not found");
    Check((await auth.UpdateTeamMemberAsync(employee.Id, new UpdateTeamMemberDto { Email = employee.Email!, Role = "ProjectManager" }, firstCompany.Id)).Succeeded, "Role change after removal succeeds");
    Check((await service.AssignAsync(Dto(employee.Id), firstCompany.Id)).Success, "Reassign after role change");
    Check((await service.GetByProjectAsync(project.Id, firstCompany.Id)).Data!.Single().Role == "ProjectManager", "Reassigned employee displays current role");
    await service.RemoveAsync((await service.GetByProjectAsync(project.Id, firstCompany.Id)).Data!.Single().Id, firstCompany.Id);

    // Force both requests past the pre-check; the real SQL unique index arbitrates the insert.
    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int arrived = 0;
    async Task Gate() { if (Interlocked.Increment(ref arrived) == 2) ready.SetResult(); await ready.Task; }
    async Task<AssignmentResult<int>> ConcurrentAssign()
    {
        await using var concurrentScope = provider.CreateAsyncScope();
        var context = concurrentScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userReader = new ProjectAssignmentUserReader(concurrentScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
        var worker = new ProjectAssignmentService(new ProjectRepository(context), new GatedRepository(new ProjectAssignmentRepository(context), Gate), userReader);
        return await worker.AssignAsync(Dto(employee.Id), firstCompany.Id);
    }
    var results = await Task.WhenAll(ConcurrentAssign(), ConcurrentAssign());
    Check(results.Count(r => r.Success) == 1 && results.Count(r => r.Error == AssignmentError.AlreadyAssigned) == 1, "Simultaneous duplicate requests: one success, one conflict");
    var api = new ApiController(service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("company_id", firstCompany.Id.ToString()) }, "test")) } } };
    Check(await api.Assign(Dto(employee.Id)) is ConflictObjectResult, "Duplicate mapped to HTTP 409");
    api.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
    Check(await api.GetByProject(project.Id) is ForbidResult, "Missing company claim is forbidden");
    Check(typeof(ApiController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles == "CompanyAdmin", "API requires CompanyAdmin authorization");
    Check(typeof(ConstructionSaaS.Web.Controllers.ProjectAssignmentsController).GetMethod("Remove")!.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true), "Remove form requires antiforgery validation");
    var currentAssignment = (await service.GetByProjectAsync(project.Id, firstCompany.Id)).Data!.Single();
    Check((await service.RemoveAsync(currentAssignment.Id, firstCompany.Id)).Success, "Prepare clean project for HTTP scenarios");
    await HttpAssignmentChecks.RunAsync(connection, project.Id, firstCompany.Id, employee.Id, inactive.Id, outsider.Id, Check);
    await WorkspaceHttpChecks.RunAsync(connection, project.Id, firstCompany.Id, otherCompany.Id, admin.Id, inactive.Id, Check);
    // A database-side rejection must roll back assignment removal too.
    await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [RejectProjectDelete] ON [Projects] INSTEAD OF DELETE AS THROW 51000, 'Test delete rejected', 1;");
    bool rejected = false;
    try { await new ProjectService(new ProjectRepository(db)).DeleteAsync(project.Id, firstCompany.Id); }
    catch (Microsoft.Data.SqlClient.SqlException) { rejected = true; }
    Check(rejected && await db.ProjectAssignments.AnyAsync(a => a.ProjectId == project.Id), "Failed project deletion rolls back assignment removal");
    await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [RejectProjectDelete]");
    Check(!await new ProjectService(new ProjectRepository(db)).DeleteAsync(project.Id, otherCompany.Id), "Cross-company project deletion rejected");
    Check(await new ProjectService(new ProjectRepository(db)).DeleteAsync(project.Id, firstCompany.Id), "Delete project with existing assignments");
    Check(!await db.ProjectAssignments.AnyAsync(a => a.ProjectId == project.Id) && !await db.Projects.AnyAsync(p => p.Id == project.Id), "Project and assignments deleted together");
    Check(!await db.ProjectMilestones.AnyAsync(m => m.ProjectId == project.Id) && !await db.ProjectDocuments.AnyAsync(d => d.ProjectId == project.Id), "Project deletion cascades to milestones and documents");
    Check(!await db.SiteWorkEntries.AnyAsync(e => e.ProjectId == project.Id), "Project deletion removes work notes, evidence and daily reports");
    Check(await db.WorkspaceActivities.AnyAsync(a => a.CompanyId == firstCompany.Id && a.Description.StartsWith("Deleted project")), "Project deletion retains audit history");
    Console.WriteLine($"ALL {passed} CHECKS PASSED");
}
finally
{
    if (!db.Database.GetDbConnection().Database.StartsWith("ConstructionSaaS_AssignmentChecks_", StringComparison.Ordinal))
        throw new InvalidOperationException("Refusing cleanup outside isolated test database.");
    await db.Database.EnsureDeletedAsync();
}

sealed class GatedRepository(IProjectAssignmentRepository inner, Func<Task> gate) : IProjectAssignmentRepository
{
    public async Task<int> AddAsync(ProjectAssignment assignment) { await gate(); return await inner.AddAsync(assignment); }
    public Task<List<ProjectAssignment>> GetByProjectAsync(int id, int company) => inner.GetByProjectAsync(id, company);
    public Task<ProjectAssignment?> GetByIdAsync(int id, int company) => inner.GetByIdAsync(id, company);
    public Task<bool> ExistsAsync(int project, string user, int company) => inner.ExistsAsync(project, user, company);
    public Task DeleteAsync(ProjectAssignment assignment) => inner.DeleteAsync(assignment);
}




