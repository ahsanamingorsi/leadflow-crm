using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

public sealed class AuditQuery : PageQuery
{
    [StringLength(120)] public string? EventType { get; init; }
    [StringLength(80)] public string? TargetType { get; init; }
    public Guid? ActorUserId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}
public sealed record AuditResponse(Guid Id, string ActorType, Guid? ActorUserId, string? ActorName, string EventType,
    string TargetType, string TargetId, DateTime OccurredAtUtc, JsonNode? Before, JsonNode? After);

[Route("api/audit")]
[Authorize(Policy = PermissionCatalog.AuditView)]
public sealed class AuditController(CrmService crm) : CrmControllerBase(crm)
{
    [HttpGet]
    public async Task<ActionResult<PageResult<AuditResponse>>> List([FromQuery] AuditQuery request, CancellationToken cancellationToken)
    {
        if (request.FromUtc > request.ToUtc) throw new CrmException(400, "The start date must be before the end date.");
        var query = Crm.Db.AuditLogs.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId);
        if (!string.IsNullOrWhiteSpace(request.EventType)) query = query.Where(x => x.EventType.Contains(request.EventType.Trim()));
        if (!string.IsNullOrWhiteSpace(request.TargetType)) query = query.Where(x => x.TargetType == request.TargetType.Trim());
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.TargetId.Contains(term) || x.EventType.Contains(term) || x.TargetType.Contains(term));
        }
        if (request.ActorUserId.HasValue) query = query.Where(x => x.ActorUserId == request.ActorUserId);
        if (request.FromUtc.HasValue) query = query.Where(x => x.OccurredAtUtc >= request.FromUtc);
        if (request.ToUtc.HasValue) query = query.Where(x => x.OccurredAtUtc < request.ToUtc);
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.OccurredAtUtc).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        var actorIds = rows.Select(x => x.ActorUserId).OfType<Guid>().Distinct().ToArray();
        var actors = await Crm.Db.Users.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && actorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        return Ok(new PageResult<AuditResponse>(rows.Select(x => new AuditResponse(x.Id, x.ActorType, x.ActorUserId,
            x.ActorUserId.HasValue ? actors.GetValueOrDefault(x.ActorUserId.Value) : "System",
            x.EventType, x.TargetType, x.TargetId, DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc),
            Redact(x.TargetType, x.BeforeJson), Redact(x.TargetType, x.AfterJson))).ToArray(), count, request.Page, request.PageSize));
    }

    private JsonNode? Redact(string target, string? json)
    {
        if (json is null) return null;
        var node = JsonNode.Parse(json);
        if (target == "Opportunity" && !Crm.Can(PermissionCatalog.OpportunityValueView) && node is JsonObject obj) obj.Remove("Value");
        return node;
    }
}
