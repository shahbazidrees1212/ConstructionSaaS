using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Application.Services;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using ConstructionSaaS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

static class HttpAssignmentChecks
{
    public static async Task RunAsync(string connection, int projectId, int companyId,
        string employeeId, string inactiveId, string outsiderId, Action<bool, string> check)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
        builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
        builder.Services.AddScoped<IProjectAssignmentRepository, ProjectAssignmentRepository>();
        builder.Services.AddScoped<IProjectAssignmentUserReader, ProjectAssignmentUserReader>();
        builder.Services.AddScoped<IProjectAssignmentService, ProjectAssignmentService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(ConstructionSaaS.API.Controllers.ProjectAssignmentsController).Assembly);
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = key,
                ValidateIssuer = true, ValidIssuer = "assignment-checks",
                ValidateAudience = true, ValidAudience = "assignment-checks",
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero
            });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            void SignIn(string role, bool companyClaim = true)
            {
                var claims = new List<Claim> { new(ClaimTypes.Role, role) };
                if (companyClaim) claims.Add(new Claim("company_id", companyId.ToString()));
                var token = new JwtSecurityToken("assignment-checks", "assignment-checks", claims,
                    expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            }
            Task<HttpResponseMessage> Assign(string id) => client.PostAsJsonAsync("/api/ProjectAssignments", new { projectId, userId = id });
            using (var anonymous = await Assign(employeeId)) check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "HTTP anonymous assignment -> 401");
            SignIn("SiteEngineer");
            using (var forbidden = await Assign(employeeId)) check(forbidden.StatusCode == HttpStatusCode.Forbidden, "HTTP non-admin assignment -> 403");
            SignIn("CompanyAdmin", false);
            using (var missingCompany = await Assign(employeeId)) check(missingCompany.StatusCode == HttpStatusCode.Forbidden, "HTTP missing company claim -> 403");
            SignIn("CompanyAdmin");
            using (var inactive = await Assign(inactiveId)) check(inactive.StatusCode == HttpStatusCode.BadRequest, "HTTP inactive employee blocked -> 400");
            using (var outsider = await Assign(outsiderId)) check(outsider.StatusCode == HttpStatusCode.NotFound, "HTTP other-company employee blocked -> 404");
            var before = await client.GetFromJsonAsync<JsonElement>($"/api/ProjectAssignments/project/{projectId}");
            check(before.GetArrayLength() == 0, "HTTP rejected requests leave no assignments");
            using var created = await Assign(employeeId);
            check(created.StatusCode == HttpStatusCode.Created, "HTTP assign employee -> 201");
            var payload = await created.Content.ReadFromJsonAsync<JsonElement>();
            var id = payload.GetProperty("id").GetInt32();
            using (var duplicate = await Assign(employeeId)) check(duplicate.StatusCode == HttpStatusCode.Conflict, "HTTP duplicate assignment -> 409");
            var assigned = await client.GetFromJsonAsync<JsonElement>($"/api/ProjectAssignments/project/{projectId}");
            check(assigned.GetArrayLength() == 1 && assigned[0].GetProperty("id").GetInt32() == id, "HTTP duplicate leaves exactly one assignment");
            using (var removed = await client.DeleteAsync($"/api/ProjectAssignments/{id}")) check(removed.StatusCode == HttpStatusCode.NoContent, "HTTP remove assignment -> 204");
            var after = await client.GetFromJsonAsync<JsonElement>($"/api/ProjectAssignments/project/{projectId}");
            check(after.GetArrayLength() == 0, "HTTP removed assignment is absent from list");
            using (var repeated = await client.DeleteAsync($"/api/ProjectAssignments/{id}")) check(repeated.StatusCode == HttpStatusCode.NotFound, "HTTP repeated remove -> 404");
            using var reassigned = await Assign(employeeId);
            check(reassigned.StatusCode == HttpStatusCode.Created, "HTTP reassign after removal -> 201");
        }
        finally { await app.StopAsync(); }
    }
}
