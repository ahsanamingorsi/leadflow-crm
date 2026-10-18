using leadflow_crm.API.Authorization;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/companies")]
public sealed class CompaniesController(CrmService crm) : CrmControllerBase(crm)
{
    [HttpGet, Authorize(Policy = PermissionCatalog.CompaniesView)]
    public async Task<ActionResult<PageResult<CompanyResponse>>> List([FromQuery] PageQuery request)
    {
        var query = Crm.Records<Company>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search)) query = query.Where(x => x.Name.Contains(request.Search.Trim()));
        var count = await query.CountAsync();
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();
        return Ok(new PageResult<CompanyResponse>(rows.Select(Crm.View).ToArray(), count, request.Page, request.PageSize));
    }

    [HttpGet("{id:guid}"), Authorize(Policy = PermissionCatalog.CompaniesView)]
    public async Task<ActionResult<CompanyResponse>> Get(Guid id) => Ok(Crm.View(await Crm.FindAsync<Company>(id)));

    [HttpPost, Authorize(Policy = PermissionCatalog.CompaniesCreate)]
    public async Task<ActionResult<CompanyResponse>> Create(CompanyRequest request)
    {
        var row = new Company { WorkspaceId = Crm.Actor.WorkspaceId };
        await ApplyAsync(row, request);
        Crm.Db.Set<Company>().Add(row);
        Crm.Track(row, "crm.company.created", type: CrmEntityType.Company, summary: "Company created.");
        await Crm.SaveAsync();
        return CreatedAtAction(nameof(Get), new { id = row.Id }, Crm.View(row));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = PermissionCatalog.CompaniesEdit)]
    public async Task<ActionResult<CompanyResponse>> Update(Guid id, CompanyRequest request)
    {
        var row = await Crm.FindAsync<Company>(id);
        CrmService.CheckVersion(row, request.RowVersion);
        var before = CrmService.Snapshot(row);
        await ApplyAsync(row, request);
        Crm.Track(row, "crm.company.updated", before, CrmEntityType.Company, "Company details updated.");
        await Crm.SaveAsync();
        return Ok(Crm.View(row));
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = PermissionCatalog.CompaniesDelete)]
    public async Task<IActionResult> Delete(Guid id, [FromHeader(Name = "If-Match")] string? rowVersion)
    {
        var row = await Crm.FindAsync<Company>(id);
        CrmService.CheckHeaderVersion(row, rowVersion);
        if (await Crm.Records<Contact>().AnyAsync(x => x.CompanyId == id))
            throw new CrmException(409, "Move or remove this company's contacts before deleting it.");
        var before = CrmService.Snapshot(row);
        row.IsDeleted = true;
        Crm.Track(row, "crm.company.deleted", before, CrmEntityType.Company, "Company deleted.");
        await Crm.SaveAsync();
        return NoContent();
    }

    private async Task ApplyAsync(Company row, CompanyRequest request)
    {
        row.CustomFieldsJson = await Crm.ValidateFieldsAsync(CrmEntityType.Company, null, request.CustomFields);
        row.Name = request.Name.Trim(); row.Email = CrmService.Clean(request.Email);
        row.Phone = CrmService.Clean(request.Phone); row.Website = CrmService.Clean(request.Website); row.Address = CrmService.Clean(request.Address);
    }
}
