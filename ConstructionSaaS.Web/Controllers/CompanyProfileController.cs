
using ConstructionSaaS.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ConstructionSaaS.Web.Controllers;

public class CompanyProfileController : Controller
{
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly IConfiguration _configuration;
    private string ApiUrl => ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(_configuration) + "/Companies/me";

    public CompanyProfileController(
        IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    private HttpClient CreateAuthorizedClient(string token)
    {
        var client = _httpClientFactory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction("Login", "Account");

        try
        {
            var client = CreateAuthorizedClient(token);

            var response = await client.GetAsync(ApiUrl);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                HttpContext.Session.Remove("JwtToken");
                return RedirectToAction("Login", "Account");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403, "Access denied.");

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound("Company profile not found.");

            if (!response.IsSuccessStatusCode)
            {
                ViewBag.ErrorMessage =
                    "Unable to load company profile.";

                return View(new CompanyProfileViewModel());
            }

            var company = await response.Content
                .ReadFromJsonAsync<CompanyProfileViewModel>();

            if (company == null)
                return NotFound("Company profile not found.");

            return View(company);
        }
        catch (HttpRequestException)
        {
            ViewBag.ErrorMessage =
                "Cannot connect to the API server.";

            return View(new CompanyProfileViewModel());
        }
        catch (JsonException)
        {
            ViewBag.ErrorMessage =
                "Invalid company data received from API.";

            return View(new CompanyProfileViewModel());
        }
        catch (TaskCanceledException)
        {
            ViewBag.ErrorMessage =
                "Company API request timed out.";

            return View(new CompanyProfileViewModel());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        CompanyProfileViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var token = HttpContext.Session.GetString("JwtToken");

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToAction("Login", "Account");

        try
        {
            var client = CreateAuthorizedClient(token);

            // Only Name is editable in UpdateCompanyDto.
            var updateDto = new
            {
                Name = model.Name.Trim()
            };

            var response = await client.PutAsJsonAsync(
                ApiUrl,
                updateDto);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                HttpContext.Session.Remove("JwtToken");
                return RedirectToAction("Login", "Account");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403, "Access denied.");

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound("Company profile not found.");

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to update company profile.");

                return View(model);
            }

            TempData["SuccessMessage"] =
                "Company profile updated successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Cannot connect to the API server.");
        }
        catch (JsonException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Invalid API response.");
        }
        catch (TaskCanceledException)
        {
            ModelState.AddModelError(
                string.Empty,
                "Company API request timed out.");
        }

        return View(model);
    }
}
