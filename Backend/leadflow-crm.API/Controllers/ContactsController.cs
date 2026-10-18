using leadflow_crm.API.Authorization;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/contacts")]
public sealed class ContactsController(CrmService crm) : CrmControllerBase(crm)
{
    [HttpGet, Authorize(Policy = PermissionCatalog.ContactsView)]
    public async Task<ActionResult<PageResult<ContactResponse>>> List([FromQuery] RecordQuery request)
    {
        var query = Crm.Records<Contact>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.DisplayName.Contains(term) || (x.Email != null && x.Email.Contains(term)) || (x.Phone != null && x.Phone.Contains(term)));
        }
        if (request.CompanyId.HasValue) query = query.Where(x => x.CompanyId == request.CompanyId);
        if (request.OwnerUserId.HasValue) query = query.Where(x => x.OwnerUserId == request.OwnerUserId);
        query = CrmQueries.ByCreatedDate(query, request);
        var count = await query.CountAsync();
        var rows = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();
        return Ok(new PageResult<ContactResponse>(rows.Select(Crm.View).ToArray(), count, request.Page, request.PageSize));
    }

    [HttpGet("{id:guid}"), Authorize(Policy = PermissionCatalog.ContactsView)]
    public async Task<ActionResult<ContactResponse>> Get(Guid id) => Ok(Crm.View(await Crm.FindAsync<Contact>(id)));

    [HttpPost, Authorize(Policy = PermissionCatalog.ContactsCreate)]
    public async Task<ActionResult<ContactResponse>> Create(ContactRequest request)
    {
        var row = new Contact { WorkspaceId = Crm.Actor.WorkspaceId };
        await ApplyAsync(row, request);
        Crm.Db.Set<Contact>().Add(row);
        Crm.Track(row, "crm.contact.created", type: CrmEntityType.Contact, summary: "Contact created.");
        await Crm.SaveAsync();
        return CreatedAtAction(nameof(Get), new { id = row.Id }, Crm.View(row));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = PermissionCatalog.ContactsEdit)]
    public async Task<ActionResult<ContactResponse>> Update(Guid id, ContactRequest request)
    {
        var row = await Crm.FindAsync<Contact>(id);
        CrmService.CheckVersion(row, request.RowVersion);
        var before = CrmService.Snapshot(row);
        await ApplyAsync(row, request);
        Crm.Track(row, "crm.contact.updated", before, CrmEntityType.Contact, "Contact details updated.");
        await Crm.SaveAsync();
        return Ok(Crm.View(row));
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = PermissionCatalog.ContactsDelete)]
    public async Task<IActionResult> Delete(Guid id, [FromHeader(Name = "If-Match")] string? rowVersion)
    {
        var row = await Crm.FindAsync<Contact>(id);
        CrmService.CheckHeaderVersion(row, rowVersion);
        if (await Crm.Records<Opportunity>().AnyAsync(x => x.ContactId == id))
            throw new CrmException(409, "Move or remove this contact's opportunities before deleting it.");
        var before = CrmService.Snapshot(row);
        row.IsDeleted = true;
        Crm.Track(row, "crm.contact.deleted", before, CrmEntityType.Contact, "Contact deleted.");
        await Crm.SaveAsync();
        return NoContent();
    }

    private async Task ApplyAsync(Contact row, ContactRequest request)
    {
        if (request.CompanyId.HasValue) await Crm.FindAsync<Company>(request.CompanyId.Value);
        await Crm.ValidateOwnerAsync(request.OwnerUserId);
        row.CustomFieldsJson = await Crm.ValidateFieldsAsync(CrmEntityType.Contact, null, request.CustomFields);
        row.DisplayName = request.DisplayName.Trim(); row.Email = CrmService.Clean(request.Email);
        row.Phone = CrmService.Clean(request.Phone); row.CompanyId = request.CompanyId;
        row.OwnerUserId = request.OwnerUserId; row.OptedOut = request.OptedOut;
    }
}
