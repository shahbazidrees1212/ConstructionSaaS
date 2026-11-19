using ConstructionSaaS.Application.DTOs.Auth;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
namespace ConstructionSaaS.Web.Controllers;
public class PasswordRecoveryController(IHttpClientFactory factory, IConfiguration config) : Controller
{
    [HttpGet] public IActionResult Forgot() => View(new ForgotPasswordInput());
    [HttpGet] public IActionResult Reset(string? email, string? token) {
        Response.Headers.CacheControl = "no-store"; Response.Headers["Referrer-Policy"] = "no-referrer";
        return View(new ResetPasswordInput { Email = email ?? "", Token = token ?? "" });
    }
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Forgot(ForgotPasswordInput input) => Submit("forgot", input, "Forgot");
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Reset(ResetPasswordInput input) => Submit("reset", input, "Reset");
    private async Task<IActionResult> Submit(string action, object input, string view)
    {
        Response.Headers.CacheControl = "no-store"; Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!ModelState.IsValid) return View(view, input);
        try {
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync(ConstructionSaaS.Web.Infrastructure.ApiEndpoint.Resolve(config) + "/password-recovery/" + action, input);
            var message = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? "Too many attempts. Wait a minute and try again." : "Request could not be completed. Please try again later.";
            try { var json = await response.Content.ReadFromJsonAsync<JsonElement>(); if (json.TryGetProperty("message", out var text)) message = text.GetString() ?? message; } catch (JsonException) { }
            if (response.IsSuccessStatusCode) { if (action == "reset") { HttpContext.Session.Clear(); TempData["LoginMessage"] = message; return RedirectToAction("Login", "Account"); } ViewData["RecoverySent"] = message; }
            else ModelState.AddModelError("", message);
        } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { ModelState.AddModelError("", "Password recovery is temporarily unavailable. Please try again later."); }
        return View(view, input);
    }
}
