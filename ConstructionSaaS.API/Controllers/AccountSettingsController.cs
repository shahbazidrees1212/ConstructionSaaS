using System.Security.Claims;
using ConstructionSaaS.Application.DTOs.Workspace;
using ConstructionSaaS.Infrastructure.Identity;
using ConstructionSaaS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Controllers;
[ApiController, Route("api/account"), Authorize(Roles = "CompanyAdmin,ProjectManager,SiteEngineer")]
public class AccountSettingsController(UserManager<ApplicationUser> users, ApplicationDbContext db) : ControllerBase
{
    private async Task<ApplicationUser?> CurrentUser()
    {
        var user = await users.GetUserAsync(User);
        return user?.IsActive == true && user.CompanyId?.ToString() == User.FindFirstValue("company_id") ? user : null;
    }
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var user = await CurrentUser();
        if (user == null) return Forbid();
        var profile = await db.AccountProfiles.AsNoTracking().Where(p => p.UserId == user.Id)
            .Select(p => new AccountDto { FullName = p.FullName, JobTitle = p.JobTitle, Phone = p.Phone,
                Location = p.Location, Bio = p.Bio, HasPhoto = p.Photo != null }).SingleOrDefaultAsync() ?? new AccountDto();
        profile.Email = user.Email ?? "";
        profile.Role = string.Join(", ", await users.GetRolesAsync(user));
        profile.CompanyName = await db.Companies.Where(c => c.Id == user.CompanyId).Select(c => c.Name).FirstOrDefaultAsync() ?? "";
        return Ok(profile);
    }
    [HttpPost("profile")]
    public async Task<IActionResult> Profile(ProfileInput input)
    {
        var user = await CurrentUser(); if (user == null) return Forbid();
        var profile = await db.AccountProfiles.FindAsync(user.Id);
        if (profile == null) { profile = new() { UserId = user.Id }; db.AccountProfiles.Add(profile); }
        profile.FullName = input.FullName.Trim(); profile.JobTitle = input.JobTitle?.Trim();
        profile.Phone = input.Phone?.Trim(); profile.Location = input.Location?.Trim(); profile.Bio = input.Bio?.Trim();
        await db.SaveChangesAsync(); return NoContent();
    }
    [HttpGet("photo")]
    public async Task<IActionResult> Photo()
    {
        var user = await CurrentUser(); if (user == null) return Forbid();
        var profile = await db.AccountProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (profile?.Photo == null) return NotFound();
        Response.Headers.CacheControl = "no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(profile.Photo, profile.PhotoContentType!);
    }
    [HttpPost("photo"), RequestSizeLimit(3 * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 3 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(IFormFile file)
    {
        var user = await CurrentUser(); if (user == null) return Forbid();
        if (file == null || file.Length == 0 || file.Length > 2 * 1024 * 1024)
            return BadRequest(new { message = "Choose a JPG or PNG photo up to 2 MB." });
        using var buffer = new MemoryStream(); await file.CopyToAsync(buffer);
        var bytes = buffer.ToArray();
        var type = DocumentValidation.ContentType(Path.GetFileName(file.FileName), bytes);
        if (type is not ("image/png" or "image/jpeg")) return BadRequest(new { message = "Choose a valid JPG or PNG photo. SVG and other files are not supported." });
        var profile = await db.AccountProfiles.FindAsync(user.Id);
        if (profile == null) { profile = new() { UserId = user.Id }; db.AccountProfiles.Add(profile); }
        profile.Photo = bytes; profile.PhotoContentType = type;
        await db.SaveChangesAsync(); return NoContent();
    }
    [HttpDelete("photo")]
    public async Task<IActionResult> RemovePhoto()
    {
        var user = await CurrentUser(); if (user == null) return Forbid();
        var profile = await db.AccountProfiles.FindAsync(user.Id);
        if (profile != null) { profile.Photo = null; profile.PhotoContentType = null; await db.SaveChangesAsync(); }
        return NoContent();
    }
    [HttpPost("password")]
    public async Task<IActionResult> Password(ChangePasswordInput input)
    {
        var user = await CurrentUser(); if (user == null) return Forbid();
        if (input.CurrentPassword == input.NewPassword) return BadRequest(new { message = "Choose a different password." });
        var result = await users.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword);
        return result.Succeeded ? NoContent() : BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
    }
    [HttpPost("email")]
    public async Task<IActionResult> Email(ChangeEmailInput input)
    {
        var user = await CurrentUser(); if (user == null) return Forbid();
        if (!await users.CheckPasswordAsync(user, input.CurrentPassword)) return BadRequest(new { message = "Current password is incorrect." });
        var email = input.Email.Trim();
        var existing = await users.FindByEmailAsync(email);
        if (existing != null && existing.Id != user.Id) return BadRequest(new { message = "This email address is already in use." });
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<IActionResult>(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            user.Email = email; user.UserName = email; user.EmailConfirmed = false;
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded) return BadRequest(new { message = string.Join(" ", updated.Errors.Select(e => e.Description)) });
            var stamp = await users.UpdateSecurityStampAsync(user);
            if (!stamp.Succeeded) return BadRequest(new { message = "Could not update account security." });
            await transaction.CommitAsync(); return NoContent();
        });
    }
    [HttpPost("sign-out-all")]
    public async Task<IActionResult> SignOutAll()
    {
        var user = await CurrentUser(); if (user == null) return Forbid();
        var result = await users.UpdateSecurityStampAsync(user);
        return result.Succeeded ? NoContent() : BadRequest(new { message = "Could not sign out sessions. Try again." });
    }
    [HttpPost("team/{id}/activate"), Authorize(Roles = "CompanyAdmin")]
    public async Task<IActionResult> Activate(string id)
    {
        var admin = await CurrentUser(); if (admin == null) return Forbid();
        var member = await users.FindByIdAsync(id);
        if (member == null || member.CompanyId != admin.CompanyId) return NotFound();
        var roles = await users.GetRolesAsync(member);
        if (roles.Contains("CompanyAdmin") || !roles.Any(r => r == "ProjectManager" || r == "SiteEngineer")) return BadRequest();
        member.IsActive = true;
        // Rotate the stamp along with activation so old deactivated sessions cannot resume.
        member.SecurityStamp = Guid.NewGuid().ToString();
        var result = await users.UpdateAsync(member);
        return result.Succeeded ? NoContent() : BadRequest(new { message = "Could not activate member." });
    }
}
