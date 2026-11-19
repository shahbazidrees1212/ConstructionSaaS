
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructionSaaS.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.Web.Controllers;

public class TeamMembersController : Controller
{
    private readonly IConfiguration _configuration;
    private string ApiBaseUrl => ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(_configuration) + "/Auth/team-members";

    private readonly IHttpClientFactory _httpClientFactory;

    public TeamMembersController(
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

    private string? GetToken()
    {
        return HttpContext.Session.GetString("JwtToken");
    }

    private IActionResult HandleUnauthorized()
    {
        HttpContext.Session.Remove("JwtToken");

        return RedirectToAction("Login", "Account");
    }

    private IActionResult RedirectToLogin()
    {
        return RedirectToAction("Login", "Account");
    }

    private string MemberUrl(string userId)
    {
        return $"{ApiBaseUrl}/{Uri.EscapeDataString(userId)}";
    }

    // LIST TEAM MEMBERS
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var token = GetToken();

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        var members = new List<TeamMemberViewModel>();

        try
        {
            var client = CreateAuthorizedClient(token);

            var response = await client.GetAsync(ApiBaseUrl);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403);

            if (!response.IsSuccessStatusCode)
            {
                ViewBag.ErrorMessage = "Unable to load team members.";
                return View(members);
            }

            members = await response.Content
                .ReadFromJsonAsync<List<TeamMemberViewModel>>()
                ?? new List<TeamMemberViewModel>();
        }
        catch (HttpRequestException)
        {
            ViewBag.ErrorMessage = "Cannot connect to API server.";
        }
        catch (JsonException)
        {
            ViewBag.ErrorMessage = "Invalid response from API.";
        }

        return View(members);
    }

    // CREATE - GET
    [HttpGet]
    public IActionResult Create()
    {
        if (string.IsNullOrWhiteSpace(GetToken()))
            return RedirectToLogin();

        return View(new CreateTeamMemberViewModel());
    }

    // CREATE - POST
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateTeamMemberViewModel model)
    {
        var token = GetToken();

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        if (model.Role != "ProjectManager" &&
            model.Role != "SiteEngineer")
        {
            ModelState.AddModelError(
                nameof(model.Role), "Select a valid role.");
        }

        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var client = CreateAuthorizedClient(token);

            var payload = new
            {
                model.Email,
                model.Password,
                model.Role
            };

            var response = await client.PostAsJsonAsync(
                ApiBaseUrl, payload);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403);

            if (!response.IsSuccessStatusCode)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "Unable to create team member. Check email, password and role.");

                return View(model);
            }

            TempData["SuccessMessage"] =
                "Team member created successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(
                string.Empty, "Cannot connect to API server.");

            return View(model);
        }
    }

    // EDIT - GET
    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var token = GetToken();

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        if (string.IsNullOrWhiteSpace(id))
            return BadRequest();

        try
        {
            var client = CreateAuthorizedClient(token);

            // Current API exposes a list, not a single-member GET.
            var response = await client.GetAsync(ApiBaseUrl);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403);

            if (!response.IsSuccessStatusCode)
                return StatusCode(502, "Unable to load team members.");

            var members = await response.Content
                .ReadFromJsonAsync<List<TeamMemberViewModel>>();

            var member = members?.FirstOrDefault(x => x.Id == id);

            if (member == null)
                return NotFound();

            return View(new EditTeamMemberViewModel
            {
                Id = member.Id,
                Email = member.Email,
                Role = member.Role
            });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, "Cannot connect to API server.");
        }
        catch (JsonException)
        {
            return StatusCode(502, "Invalid API response.");
        }
    }

    // EDIT - POST
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        string id,
        EditTeamMemberViewModel model)
    {
        var token = GetToken();

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        if (string.IsNullOrWhiteSpace(id) || id != model.Id)
            return BadRequest();

        if (model.Role != "ProjectManager" &&
            model.Role != "SiteEngineer")
        {
            ModelState.AddModelError(
                nameof(model.Role), "Select a valid role.");
        }

        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var client = CreateAuthorizedClient(token);

            var payload = new
            {
                model.Email,
                model.Role
            };

            var response = await client.PutAsJsonAsync(
                MemberUrl(id), payload);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound();

            if (!response.IsSuccessStatusCode)
            {
                var message = "Unable to update team member.";
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    try
                    {
                        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                        if (body.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
                        {
                            var messages = errors.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String)
                                .Select(e => e.GetString()).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
                            if (messages.Count > 0) message = string.Join(" ", messages);
                        }
                    }
                    catch (JsonException) { /* Retain the fallback for malformed responses. */ }
                }
                ModelState.AddModelError(string.Empty, message);

                return View(model);
            }

            TempData["SuccessMessage"] =
                "Team member updated successfully.";

            return RedirectToAction(nameof(Index));
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError(
                string.Empty, "Cannot connect to API server.");

            return View(model);
        }
    }

    // DEACTIVATE - POST
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(string id)
    {
        var token = GetToken();

        if (string.IsNullOrWhiteSpace(token))
            return RedirectToLogin();

        if (string.IsNullOrWhiteSpace(id))
            return BadRequest();

        try
        {
            var client = CreateAuthorizedClient(token);

            var response = await client.PatchAsync(
                $"{MemberUrl(id)}/deactivate", null);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return HandleUnauthorized();

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return StatusCode(403);

            if (!response.IsSuccessStatusCode)
            {
                TempData["ErrorMessage"] =
                    "Unable to deactivate team member.";

                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] =
                "Team member deactivated successfully.";
        }
        catch (HttpRequestException)
        {
            TempData["ErrorMessage"] =
                "Cannot connect to API server.";
        }

        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(string id)
    {
        var token = GetToken();
        if (string.IsNullOrWhiteSpace(token)) return RedirectToLogin();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        try
        {
            using var client = CreateAuthorizedClient(token);
            using var response = await client.PostAsync($"{ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(_configuration)}/account/team/{Uri.EscapeDataString(id)}/activate", null);
            if (response.StatusCode == HttpStatusCode.Unauthorized) return HandleUnauthorized();
            if (response.StatusCode == HttpStatusCode.Forbidden) return StatusCode(403);
            TempData[response.IsSuccessStatusCode ? "SuccessMessage" : "ErrorMessage"] = response.IsSuccessStatusCode ? "Team member activated. They can sign in again." : "Could not activate this team member.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { TempData["ErrorMessage"] = "The API is unavailable. Please try again."; }
        return RedirectToAction(nameof(Index));
    }}


