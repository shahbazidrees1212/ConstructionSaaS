using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static class WorkspaceWebChecks
{
    public static async Task RunAsync(string apiUrl, string token, int projectId, Action<bool, string> check, bool manager = false)
    {
        var controller = manager ? "ManagerWorkspace" : "Workspace";
        var root = Path.GetFullPath("ConstructionSaaS.Web");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = root, ApplicationName = typeof(ConstructionSaaS.Web.Controllers.WorkspaceController).Assembly.FullName });
        builder.Logging.ClearProviders();
        builder.Configuration["Api:BaseUrl"] = apiUrl + "/api";
        builder.Services.AddControllersWithViews(o => o.Filters.Add<ConstructionSaaS.Web.Infrastructure.SubscriptionAccessFilter>()).AddApplicationPart(typeof(ConstructionSaaS.Web.Controllers.WorkspaceController).Assembly);
        builder.Services.AddHttpClient(); builder.Services.AddDistributedMemoryCache(); builder.Services.AddSession();
        await using var app = builder.Build(); app.Urls.Add("http://127.0.0.1:0");
        app.UseStaticFiles(); app.UseRouting(); app.UseSession();
        // Test-only session fixture. Production has no such middleware or header.
        app.Use(async (context, next) => { if (context.Request.Headers.ContainsKey("X-Workspace-Test")) context.Session.SetString("JwtToken", token); await next(); });
        app.MapControllerRoute("default", "{controller=Workspace}/{action=Schedule}/{id?}");
        await app.StartAsync();
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(app.Urls.Single()) };
            using (var anonymous = await client.GetAsync($"/{controller}/Schedule")) check(anonymous.StatusCode == HttpStatusCode.Found && anonymous.Headers.Location!.ToString().Contains("Account/Login"), "Web workspace requires login");
            client.DefaultRequestHeaders.Add("X-Workspace-Test", "1");
            if (manager) {
                foreach (var route in new[] { "/ManagerDashboard/Index", "/ManagerProjects/Index", $"/ManagerProjects/Details/{projectId}" }) {
                    using var pageResponse = await client.GetAsync(route);
                    var content = await pageResponse.Content.ReadAsStringAsync();
                    check(pageResponse.IsSuccessStatusCode && content.Contains("My profile") && !content.Contains(">Privacy<"), $"Manager page {route} renders shared professional shell");
                }
                using var adminPage = await client.GetAsync("/Workspace/Settings");
                check(adminPage.StatusCode == HttpStatusCode.Forbidden, "Manager cannot enter admin MVC workspace");
            }
            if (!manager) {
                foreach (var route in new[] { "/Projects/Create", "/TeamMembers/Create" }) {
                    using var response = await client.GetAsync(route);
                    var html = await response.Content.ReadAsStringAsync();
                    check(response.IsSuccessStatusCode && html.Contains("workspace-polish.css") && html.Contains("workspace-ui.js") && html.Contains("__RequestVerificationToken") && !html.Contains("navbar-expand-sm"), "Admin create page uses shared UI and preserves form: " + route);
                }
            }
            if (!manager) {
                var billing = await BillingWebChecks.RunAsync(client, apiUrl, token, check);
                await BrowserChecks.RunAsync(app.Urls.Single(), "admin", projectId, check, billing.Id, billing.Link);
            }
            string schedule = "";
            foreach (var page in new[] { "Schedule", "Documents", "Activity", "Reports", "Settings" })
            {
                using var response = await client.GetAsync($"/{controller}/{page}");
                var html = await response.Content.ReadAsStringAsync();
                check(response.StatusCode == HttpStatusCode.OK && html.Contains("ConstructionSaaS") && !html.Contains("storage is being connected"), $"Web {page} renders real data and shared navigation");
                if (manager) {
                    check(html.Contains("Profile &amp; security") && html.Contains("My projects") && !html.Contains("Manage team access"), $"Manager {page} uses manager navigation");
                    check(!html.Contains("We couldn&#x27;t load") && html.Contains("module-hero"), $"Manager {page} loads API data");
                }
                if (manager && page == "Documents") {
                    foreach (var file in new[] { "admin-owned.txt", "other-manager.txt", "legacy.txt" }) {
                        var row = Regex.Matches(html, @"<tr\b[^>]*>.*?</tr>", RegexOptions.Singleline).Select(m => m.Value).SingleOrDefault(r => r.Contains("<strong>" + file + "</strong>")) ?? "";
                        check(row.Contains("Protected file") && !row.Contains("DeleteDocument"), $"Manager UI protects {file}");
                    }
                    var ownRow = Regex.Matches(html, @"<tr\b[^>]*>.*?</tr>", RegexOptions.Singleline).Select(m => m.Value).SingleOrDefault(r => r.Contains("<strong>manager.txt</strong>")) ?? "";
                    check(ownRow.Contains("DeleteDocument"), "Manager UI offers Remove for own upload");
                }
                if (page == "Schedule") schedule = html;
            }
            using (var csrf = await client.PostAsync($"/{controller}/SaveMilestone", new FormUrlEncodedContent(new Dictionary<string,string> { ["projectId"] = projectId.ToString(), ["Title"] = "Invalid csrf", ["Progress"] = "0", ["DueDateUtc"] = "2030-01-01" })))
                check(csrf.StatusCode == HttpStatusCode.BadRequest, "Web milestone form enforces antiforgery");
            var match = Regex.Match(schedule, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            check(match.Success, "Rendered form includes antiforgery token");
            var values = new Dictionary<string,string> { ["projectId"] = projectId.ToString(), ["Title"] = "Web form milestone", ["Progress"] = "25", ["DueDateUtc"] = "2030-01-01", ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value) };
            using (var posted = await client.PostAsync($"/{controller}/SaveMilestone", new FormUrlEncodedContent(values)))
                check(posted.StatusCode == HttpStatusCode.Found && posted.Headers.Location!.ToString().Contains($"projectId={projectId}"), "Web milestone form posts through to API");
            var refreshed = await client.GetStringAsync($"/{controller}/Schedule?projectId={projectId}");
            check(refreshed.Contains("Web form milestone") && refreshed.Contains("Milestone saved."), "Saved milestone and success notice appear after redirect");
            using var download = await client.GetAsync($"/{controller}/ExportReport");
            check(download.IsSuccessStatusCode && download.Content.Headers.ContentDisposition?.DispositionType == "attachment", "Web report download proxies authenticated API");
        }
        finally { await app.StopAsync(); }
    }
}

