using ConstructionSaaS.Application.DTOs.Companies;
using ConstructionSaaS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ConstructionSaaS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "CompanyAdmin")]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyService _companyService;

    public CompaniesController(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    private bool TryGetCompanyId(out int companyId)
    {
        var claimValue = User.FindFirst("company_id")?.Value;
        return int.TryParse(claimValue, out companyId);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCompanyDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var companyId = await _companyService.CreateAsync(dto, userId);

            return Ok(new
            {
                companyId,
                message = "Company created and linked to your account."
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var companies = await _companyService.GetAllAsync(companyId);
        return Ok(companies);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var company = await _companyService.GetByIdAsync(id, companyId);

        if (company == null)
            return NotFound();

        return Ok(company);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateCompanyDto dto)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var updated = await _companyService.UpdateAsync(id, dto, companyId);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Deactivate(int id)
    {
        if (!TryGetCompanyId(out var companyId))
            return Forbid();

        var deactivated =
            await _companyService.DeactivateAsync(id, companyId);

        if (!deactivated)
            return NotFound();

        return NoContent();
    }
}