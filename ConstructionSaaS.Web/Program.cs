using ConstructionSaaS.Hosting;
using ConstructionSaaS.Web.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
﻿var builder = WebApplication.CreateBuilder(args);

builder.AddProductionHosting("ConstructionSaaS.Web");
_ = ApiEndpoint.Resolve(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<ApiReadinessCheck>("api", tags: new[] { "ready" });
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);
// Add services to the container.
builder.Services.AddControllersWithViews(options => options.Filters.Add<SubscriptionAccessFilter>());
builder.Services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName, client => client.Timeout = TimeSpan.FromSeconds(30));
// PayPal verification can require multiple sequential provider calls (each capped at 30 seconds).
builder.Services.AddHttpClient("billing", client => client.Timeout = TimeSpan.FromSeconds(180));

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});
var app = builder.Build();
app.UseProductionHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.

}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
// Keep existing bookmarked notification links working without changing the login route.
app.MapControllerRoute(
    name: "task-board",
    pattern: "TaskBoard",
    defaults: new { controller = "TaskBoard", action = "Index" });
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();

