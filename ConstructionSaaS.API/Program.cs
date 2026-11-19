using ConstructionSaaS.Hosting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using ConstructionSaaS.Application.Interfaces;
using ConstructionSaaS.Application.Services;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using ConstructionSaaS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.AddProductionHosting("ConstructionSaaS.API");
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);
builder.Services.AddHealthChecks().AddCheck<ConstructionSaaS.API.Security.DatabaseReadinessCheck>("database", tags: new[] { "ready" });

// ==========================================
// 1. Controllers and Swagger
// ==========================================

builder.Services.AddControllers();
builder.Services.AddScoped<ConstructionSaaS.API.Subscriptions.SubscriptionCatalog>();
builder.Services.AddScoped<ConstructionSaaS.API.Subscriptions.ISubscriptionGateway, ConstructionSaaS.API.Subscriptions.SubscriptionGateway>();
builder.Services.AddScoped<ConstructionSaaS.API.Subscriptions.SubscriptionService>();
builder.Services.AddHostedService<ConstructionSaaS.API.Subscriptions.SubscriptionReconciler>();
builder.Services.AddHttpClient("payments", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<ConstructionSaaS.API.Payments.IPaymentGateway, ConstructionSaaS.API.Payments.PaymentGateway>();
builder.Services.AddScoped<ConstructionSaaS.API.Payments.BillingService>();
builder.Services.AddRateLimiter(o => o.AddPolicy("payments", context =>
    System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuditActor, ConstructionSaaS.API.Security.HttpAuditActor>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Login se mila JWT token yahan paste karein."
        });

    options.AddSecurityRequirement(
        new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
});

// ==========================================
// 2. SQL Server Database
// ==========================================

var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "DefaultConnection is missing in configuration.");

builder.Services.AddSingleton<ConstructionSaaS.Infrastructure.Persistence.NotificationSignal>();
builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
{
    options.AddInterceptors(services.GetRequiredService<ConstructionSaaS.Infrastructure.Persistence.NotificationSignal>());
    options.UseSqlServer(
        connectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
        });
});

// ==========================================
// 3. Dependency Injection
// ==========================================

builder.Services.AddScoped<
    ICompanyRepository,
    CompanyRepository>();

builder.Services.AddScoped<
    ICompanyService,
    CompanyService>();

builder.Services.AddScoped<
    IProjectRepository,
    ProjectRepository>();

builder.Services.AddScoped<
    IProjectService,
    ProjectService>();

builder.Services.AddScoped<
    IAuthService,
    AuthService>();

builder.Services.AddScoped<
    ITokenService,
    JwtTokenService>();

builder.Services.AddScoped<
    IProjectAssignmentUserReader,
    ProjectAssignmentUserReader>();

builder.Services.AddScoped<
    IProjectAssignmentService,
    ProjectAssignmentService>();
builder.Services.AddScoped<
    IProjectAssignmentRepository,
    ProjectAssignmentRepository>();
builder.Services.AddScoped<
    IProjectTaskRepository,
    ProjectTaskRepository>();

builder.Services.AddScoped<
    IProjectTaskAccessRepository,
    ProjectTaskAccessRepository>();

builder.Services.AddScoped<
    IProjectTaskService,
    ProjectTaskService>();
// ==========================================
// 4. ASP.NET Core Identity
// ==========================================

builder.Services.AddDataProtection();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ==========================================
// 5. JWT Authentication
// ==========================================

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException(
        "JWT key is missing.");

if (Encoding.UTF8.GetByteCount(jwtKey) < 32) throw new InvalidOperationException("JWT signing key must contain at least 32 bytes.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException(
        "JWT issuer is missing.");

var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException(
        "JWT audience is missing.");

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,

                ValidateAudience = true,
                ValidAudience = jwtAudience,

                ValidateLifetime = true,

                ValidateIssuerSigningKey = true,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey))
            };

        // ==========================================
        // 6. Check User Status on Every JWT Request
        // ==========================================

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ConstructionSaaS.API.Security.WorkspaceTokenValidation.ValidateAsync
        };
    });

builder.Services.AddAuthorization();

// ==========================================
// 7. Build Application
// ==========================================

builder.Services.AddScoped<ConstructionSaaS.API.Security.IPasswordResetMail, ConstructionSaaS.API.Security.PasswordResetMail>();
builder.Services.Configure<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("password-recovery", context => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();

// ==========================================
// 8. Seed Identity Roles
// ==========================================

if (app.Configuration.GetValue("Database:SeedRolesOnStartup", app.Environment.IsDevelopment()))
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole>>();

    string[] roles =
    {
        "CompanyAdmin",
        "PlatformAdmin",
        "ProjectManager",
        "SiteEngineer"
    };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var result = await roleManager.CreateAsync(
                new IdentityRole(role));

            if (!result.Succeeded)
            {
                var errors = string.Join(
                    "; ",
                    result.Errors.Select(
                        error => error.Description));

                throw new InvalidOperationException(
                    $"Failed to create role '{role}': {errors}");
            }
        }
    }
}

// ==========================================
// 9. Swagger Middleware
// ==========================================

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ==========================================
// 10. HTTP Request Pipeline
// ==========================================

app.UseProductionHeaders();
app.UseExceptionHandler();
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ConstructionSaaS.API.Subscriptions.SubscriptionAccessMiddleware>();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.MapGet(
    "/",
    () => "ConstructionSaaS API is running");

app.Run();
