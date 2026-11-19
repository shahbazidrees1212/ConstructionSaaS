
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using ConstructionSaaS.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.Web.Controllers;

public class AccountController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public AccountController(
        IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Login()
    {
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            using var client = _httpClientFactory.CreateClient();

            using var response = await client.PostAsJsonAsync(
                ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(_configuration) + "/Auth/login",
                new
                {
                    email = model.Email,
                    password = model.Password
                });

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError(
                    string.Empty,
                    response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                        ? "Too many sign-in attempts. Please wait a minute and try again."
                        : (int)response.StatusCode >= 500
                            ? "Login service is unavailable. Please try again."
                            : "Unable to sign in. Check your email and password, or try again later.");

                return View(model);
            }

            var result = await response.Content
                .ReadFromJsonAsync<LoginApiResponse>();

            if (string.IsNullOrWhiteSpace(result?.Token))
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Login failed. Token was not received.");

                return View(model);
            }

            // Read role claims for dashboard navigation.
            // JWT signature validation remains the API's responsibility.
            JwtSecurityToken jwt;

            try
            {
                var handler = new JwtSecurityTokenHandler();

                if (!handler.CanReadToken(result.Token))
                {
                    ModelState.AddModelError(
                        string.Empty,
                        "Invalid authentication token.");

                    return View(model);
                }

                jwt = handler.ReadJwtToken(result.Token);
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is Microsoft.IdentityModel.Tokens.SecurityTokenException)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to read authentication token.");

                return View(model);
            }

            if (jwt.ValidTo <= DateTime.UtcNow)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Authentication token has expired.");

                return View(model);
            }

            var roles = jwt.Claims
                .Where(c =>
                    c.Type == ClaimTypes.Role ||
                    c.Type == "role" ||
                    c.Type == "roles")
                .Select(c => c.Value)
                .ToList();

            string dashboardController;

            if (roles.Contains("PlatformAdmin", StringComparer.OrdinalIgnoreCase))
            {
                HttpContext.Session.SetString("JwtToken", result.Token);
                return RedirectToAction("Platform", "Subscription");
            }
            if (roles.Contains(
                "CompanyAdmin",
                StringComparer.OrdinalIgnoreCase))
            {
                dashboardController = "Dashboard";
            }
            else if (roles.Contains(
                "ProjectManager",
                StringComparer.OrdinalIgnoreCase))
            {
                dashboardController = "ManagerDashboard";
            }
            else if (roles.Contains(
                "SiteEngineer",
                StringComparer.OrdinalIgnoreCase))
            {
                dashboardController = "EngineerDashboard";
            }
            else
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Your account does not have an assigned dashboard role.");

                return View(model);
            }

            // Store JWT only after checking the expected claims.
            HttpContext.Session.SetString(
                "JwtToken",
                result.Token);

            TempData["LoginMessage"] = "Login successful!";

            return RedirectToAction(
                "Index",
                dashboardController);
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            ModelState.AddModelError(string.Empty, "Login service took too long to respond. Please try again.");
            return View(model);
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Login service is unavailable. Please try again.");

            return View(model);
        }
        catch (System.Text.Json.JsonException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Unexpected response received from login service.");

            return View(model);
        }
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            using var client = _httpClientFactory.CreateClient();

            using var response = await client.PostAsJsonAsync(
                ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(_configuration) + "/Auth/register",
                new
                {
                    email = model.Email,
                    password = model.Password,
                    confirmPassword = model.ConfirmPassword,
                    companyName = model.CompanyName,
                    country = model.Country
                });

            if (!response.IsSuccessStatusCode)
            {
                var message =
                    "We could not create your account. Please try again.";

                try
                {
                    var error = await response.Content
                        .ReadFromJsonAsync<RegistrationErrorResponse>();

                    message = error?.Errors?.FirstOrDefault()
                              ?? message;
                }
                catch (System.Text.Json.JsonException)
                {
                    // Keep the default error message.
                }

                ModelState.AddModelError(
                    string.Empty,
                    message);

                return View(model);
            }

            TempData["LoginMessage"] =
                "Account created successfully. Please sign in.";

            return RedirectToAction(nameof(Login));
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            ModelState.AddModelError(string.Empty, "Registration service took too long to respond. Please try again.");
            return View(model);
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Registration service is currently unavailable.");

            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();

        return RedirectToAction(
            "Login",
            "Account");
    }

    private sealed class LoginApiResponse
    {
        public string? Token { get; set; }
    }

    private sealed class RegistrationErrorResponse
    {
        public List<string>? Errors { get; set; }
    }
}
