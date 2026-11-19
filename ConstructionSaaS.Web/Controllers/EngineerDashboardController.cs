
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.Web.Controllers
{
    public class EngineerDashboardController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            // STEP 1: Get JWT from session
            var token = HttpContext.Session.GetString("JwtToken");

            if (string.IsNullOrWhiteSpace(token))
            {
                return RedirectToAction("Login", "Account");
            }

            JwtSecurityToken jwt;

            // STEP 2: Read JWT
            try
            {
                var handler = new JwtSecurityTokenHandler();

                if (!handler.CanReadToken(token))
                {
                    HttpContext.Session.Remove("JwtToken");

                    return RedirectToAction("Login", "Account");
                }

                jwt = handler.ReadJwtToken(token);
            }
            catch (Exception)
            {
                HttpContext.Session.Remove("JwtToken");

                return RedirectToAction("Login", "Account");
            }

            // STEP 3: Check token expiration
            if (jwt.ValidTo <= DateTime.UtcNow)
            {
                HttpContext.Session.Remove("JwtToken");

                return RedirectToAction("Login", "Account");
            }

            // STEP 4: Check SiteEngineer role
            var isEngineer = jwt.Claims.Any(c =>
                (c.Type == ClaimTypes.Role ||
                 c.Type == "role" ||
                 c.Type == "roles") &&
                string.Equals(
                    c.Value,
                    "SiteEngineer",
                    StringComparison.OrdinalIgnoreCase));

            if (!isEngineer)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    "Access denied. SiteEngineer role required.");
            }

            // STEP 5: Read Company ID
            var companyIdClaim = jwt.Claims
                .FirstOrDefault(c => c.Type == "company_id")
                ?.Value;

            if (!int.TryParse(
                    companyIdClaim,
                    out var companyId) ||
                companyId <= 0)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    "A valid company ID is required.");
            }

            // STEP 6: Get email
            var userEmail = jwt.Claims
                .FirstOrDefault(c =>
                    c.Type == ClaimTypes.Email ||
                    c.Type == "email")
                ?.Value;

            // STEP 7: Send information to View
            ViewBag.CompanyId = companyId;
            ViewBag.UserEmail = userEmail;

            // STEP 8: Open Engineer Dashboard
            return RedirectToAction("Index", "TaskBoard");
        }
    }
}
