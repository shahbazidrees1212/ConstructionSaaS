using System.Net.Http.Json;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static class TaskWebChecks
{
    public static async Task RunAsync(string apiUrl, string engineerToken, string managerToken, int projectId, int taskId, Action<bool,string> check)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Testing", ContentRootPath=Path.GetFullPath("ConstructionSaaS.Web"), ApplicationName=typeof(ConstructionSaaS.Web.Controllers.TaskBoardController).Assembly.FullName });
        builder.Logging.ClearProviders(); builder.Configuration["Api:BaseUrl"] = apiUrl + "/api";
        builder.Services.AddControllersWithViews(o => o.Filters.Add<ConstructionSaaS.Web.Infrastructure.SubscriptionAccessFilter>()).AddApplicationPart(typeof(ConstructionSaaS.Web.Controllers.TaskBoardController).Assembly);
        builder.Services.AddHttpClient(); builder.Services.AddDistributedMemoryCache(); builder.Services.AddSession();
        await using var app = builder.Build(); app.Urls.Add("http://127.0.0.1:0"); app.UseStaticFiles(); app.UseRouting(); app.UseSession();
        app.Use(async (context,next) => { if(context.Request.Headers.TryGetValue("X-Test-Role",out var role)) context.Session.SetString("JwtToken",role == "manager" ? managerToken : engineerToken); await next(); });
        app.MapControllerRoute("task-board", "TaskBoard", new { controller = "TaskBoard", action = "Index" });
        app.MapControllerRoute("default","{controller=Account}/{action=Login}/{id?}"); await app.StartAsync();
        try {
            using var handler = new HttpClientHandler { AllowAutoRedirect=false, CookieContainer=new() };
            using var client = new HttpClient(handler) { BaseAddress=new Uri(app.Urls.Single()) };
            using(var anonymous=await client.GetAsync("/TaskBoard")) check(anonymous.StatusCode==HttpStatusCode.Found,"Task MVC requires login");
            var recovery = await client.GetStringAsync("/PasswordRecovery/Forgot");
            check(recovery.Contains("Forgot your password?") && recovery.Contains("__RequestVerificationToken"), "Recovery form renders with antiforgery");
            using(var noCsrf = await client.PostAsync("/PasswordRecovery/Forgot", new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","unknown@example.test"}}))) check(noCsrf.StatusCode == HttpStatusCode.BadRequest, "Recovery form blocks missing CSRF");
            var recoveryToken=Regex.Match(recovery,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            using(var sent = await client.PostAsync("/PasswordRecovery/Forgot", new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","unknown@example.test"},{"__RequestVerificationToken", WebUtility.HtmlDecode(recoveryToken.Groups[1].Value)}}))) check(sent.IsSuccessStatusCode && (await sent.Content.ReadAsStringAsync()).Contains("If an active account"), "Recovery MVC proxies generic API response");
            client.DefaultRequestHeaders.Add("X-Test-Role","engineer");
            using(var subscription=await client.GetAsync("/Subscription/Index")) {
                var subscriptionHtml=await subscription.Content.ReadAsStringAsync();
                check(subscription.IsSuccessStatusCode&&!subscriptionHtml.Contains("name=\"PlanKey\""),"Engineer subscription view has no purchasing controls");
            }
            using (var shortLink = await client.GetAsync($"/TaskBoard?projectId={projectId}"))
                check(shortLink.IsSuccessStatusCode && (await shortLink.Content.ReadAsStringAsync()).Contains("My tasks"), "Bookmarked notification short link opens assigned project with production route defaults");
            var inbox = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/Notifications/Inbox");
            var notificationUrl = inbox.GetProperty("items").EnumerateArray().First().GetProperty("url").GetString()!;
            check(notificationUrl == $"/TaskBoard/Index?projectId={projectId}", "Bell generates explicit TaskBoard Index link");
            using (var destination = await client.GetAsync(notificationUrl))
                check(destination.IsSuccessStatusCode && (await destination.Content.ReadAsStringAsync()).Contains("My tasks"), "Actual notification destination renders assigned project");
            var html = await client.GetStringAsync("/TaskBoard");
            check(html.Contains("My tasks") && html.Contains("Your manager approved this work") && !html.Contains("Create a task") && !html.Contains("Private colleague task"),"Engineer UI shows own work and status controls only");
            check(html.Contains("id=\"liveNotifications\"") && html.Contains("Mark all read"), "Engineer dashboard includes live notification inbox");
            using (var bellCsrf = await client.PostAsync("/Notifications/ReadAll", null)) check(bellCsrf.StatusCode == HttpStatusCode.BadRequest, "Bell mark-all requires antiforgery");
            foreach(var tab in new[]{"notifications","reports","history","daily"}) {
                var page=await client.GetStringAsync("/TaskBoard?tab="+tab);
                check(page.Contains("Site Engineer") && !page.Contains("Company profile"),"Engineer " + tab + " renders professional role-aware shell");
            }
            using(var csrf=await client.PostAsync("/TaskBoard/Status",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",taskId.ToString()},{"projectId",projectId.ToString()},{"Status","Pending"},{"ExpectedStatus","Completed"}}))) check(csrf.StatusCode==HttpStatusCode.BadRequest,"Task status form rejects missing CSRF token");
            var match=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            check(match.Success,"Task form renders antiforgery token");
            using(var status=await client.PostAsync("/TaskBoard/Status",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",taskId.ToString()},{"projectId",projectId.ToString()},{"Status","Completed"},{"ExpectedStatus","Completed"},{"__RequestVerificationToken",WebUtility.HtmlDecode(match.Groups[1].Value)}}))) check(status.StatusCode==HttpStatusCode.Found,"Engineer status form reaches API");
            client.DefaultRequestHeaders.Remove("X-Test-Role"); client.DefaultRequestHeaders.Add("X-Test-Role","manager");
            html=await client.GetStringAsync("/TaskBoard");
            check(html.Contains("Create a task") && html.Contains("engineer@task.test") && !html.Contains("inactiveengineer@task.test"),"Manager task UI offers eligible assignees only");
            var managerCsrf=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            var invalidValues = new Dictionary<string,string>{{"ProjectId",projectId.ToString()},{"Title","Unassigned engineer attempt"},{"Priority","High"},{"AssignedToUserId","not-an-engineer"},{"__RequestVerificationToken",WebUtility.HtmlDecode(managerCsrf.Groups[1].Value)}};
            using(var invalid=await client.PostAsync("/TaskBoard/Create",new FormUrlEncodedContent(invalidValues))) check(invalid.StatusCode==HttpStatusCode.Found,"Invalid task assignee returns form feedback, not server error");
            check((await client.GetStringAsync("/TaskBoard")).Contains("Choose an active SiteEngineer"),"API validation message appears on task page");
            invalidValues["AssignedToUserId"]="";
            invalidValues["Title"]="Web-created task";
            using(var created=await client.PostAsync("/TaskBoard/Create",new FormUrlEncodedContent(invalidValues))) check(created.StatusCode==HttpStatusCode.Found,"Manager creates task through MVC form");
            check((await client.GetStringAsync("/TaskBoard")).Contains("Web-created task"),"Created task appears in manager board");
            foreach(var tab in new[]{"notifications","reports","history","daily"}) check((await client.GetStringAsync("/TaskBoard?tab="+tab)).Contains("Project Manager"),"Manager " + tab + " renders");
            await BrowserChecks.RunAsync(app.Urls.Single(), "engineer", projectId, check);
            await BrowserChecks.RunAsync(app.Urls.Single(), "manager", projectId, check);
            using var export=await client.GetAsync("/TaskBoard/Export"); check(export.IsSuccessStatusCode && export.Content.Headers.ContentDisposition?.DispositionType=="attachment","MVC task CSV download works");
        } finally { await app.StopAsync(); }
    }
}
