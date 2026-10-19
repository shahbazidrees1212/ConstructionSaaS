using ConstructionSaaS.Application.DTOs.Companies;
using ConstructionSaaS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ConstructionSaaS.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyService _companyService;

    public CompaniesController(ICompanyService companyService)
    {
        _companyService = companyService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateCompanyDto dto)
    {
        await _companyService.CreateAsync(dto);

        return Ok(new { message = "Company created successfully" });
    }
}