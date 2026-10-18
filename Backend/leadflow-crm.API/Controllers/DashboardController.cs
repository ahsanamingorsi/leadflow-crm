using leadflow_crm.API.Authorization;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/dashboard")]
public sealed class DashboardController(CrmService crm) : CrmControllerBase(crm)
{
    [HttpGet("overview"), Authorize(Policy = PermissionCatalog.DashboardView)]
    public async Task<ActionResult<DashboardOverviewResponse>> Overview(CancellationToken cancellationToken)
    {
        Crm.Require(PermissionCatalog.DashboardView);
        var db = Crm.Db;
        var workspaceId = Crm.Actor.WorkspaceId;
        var actorId = Crm.Actor.Id;
        var now = DateTime.UtcNow;
        var companyWide = Crm.Can(PermissionCatalog.LeadsViewAll);
        var leads = db.Leads.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && !x.IsDeleted);
        if (!companyWide) leads = leads.Where(x => x.OwnerUserId == actorId);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, timeZone);
        var localDayStart = DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified);
        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localDayStart, timeZone);
        var nextDayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localDayStart.AddDays(1), timeZone);
        var monthStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(new DateTime(localNow.Year, localNow.Month, 1), DateTimeKind.Unspecified), timeZone);
        var nextMonthStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(new DateTime(localNow.Year, localNow.Month, 1).AddMonths(1), DateTimeKind.Unspecified), timeZone);

        var configuration = await db.LeadAssignmentConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken);
        var idleCutoff = now.AddSeconds(-(configuration?.IdleWindowSeconds ?? 86_400));
        var newLeadsToday = await leads.CountAsync(x => x.CreatedAtUtc >= todayStartUtc && x.CreatedAtUtc < nextDayStartUtc, cancellationToken);
        var openLeads = await leads.CountAsync(x => !x.IsClosed, cancellationToken);
        var recoveryCount = await leads.CountAsync(x => !x.IsClosed &&
            ((!x.AcknowledgedAtUtc.HasValue && x.AcknowledgementDeadlineUtc <= now) || x.EscalationCount >= 2 || x.LastActionAtUtc <= idleCutoff),
            cancellationToken);

        var firstAssignments = db.LeadAssignmentHistory.AsNoTracking().Where(x => x.WorkspaceId == workspaceId)
            .GroupBy(x => x.LeadId).Select(group => new
            {
                LeadId = group.Key,
                FirstAssignedAtUtc = group.Min(x => x.AssignedAtUtc)
            });
        var responseTimes = from lead in leads
                            where !lead.IsDeleted && lead.AcknowledgedAtUtc.HasValue
                            join first in firstAssignments on lead.Id equals first.LeadId
                            select EF.Functions.DateDiffSecond(first.FirstAssignedAtUtc, lead.AcknowledgedAtUtc!.Value);
        var averageResponseSeconds = await responseTimes.Select(x => (double?)x).AverageAsync(cancellationToken);

        var escalationsQuery = db.LeadAssignmentHistory.AsNoTracking().Where(x => x.WorkspaceId == workspaceId &&
            x.Reason == "AcknowledgementExpired" && x.AssignedAtUtc >= now.AddHours(-24) &&
            leads.Any(lead => lead.Id == x.LeadId));
        var escalations = await escalationsQuery.CountAsync(cancellationToken);

        var opportunities = db.Set<Opportunity>().AsNoTracking().Where(x => x.WorkspaceId == workspaceId && !x.IsDeleted);
        if (!companyWide) opportunities = opportunities.Where(x => x.OwnerUserId == actorId);
        var canSeeValues = Crm.Can(PermissionCatalog.OpportunityValueView);
        var stageMetrics = await (from stage in db.Set<PipelineStage>().AsNoTracking()
                                  where stage.WorkspaceId == workspaceId && !stage.IsDeleted && stage.Kind == StageKind.Open
                                  join opportunity in opportunities on stage.Id equals opportunity.StageId into stageOpportunities
                                  orderby stage.Position
                                  select new DashboardStageMetric(stage.Id, stage.Name, stageOpportunities.Count(),
                                      canSeeValues ? stageOpportunities.Sum(x => (decimal?)x.Value) : null))
            .ToListAsync(cancellationToken);
        var openPipelineValue = canSeeValues
            ? stageMetrics.Sum(x => x.OpenValue ?? 0m)
            : (decimal?)null;

        var paymentQuery = db.PaymentRecords.AsNoTracking().Where(x => x.WorkspaceId == workspaceId);
        if (!companyWide)
            paymentQuery = paymentQuery.Where(payment => db.Set<Opportunity>().Any(opportunity =>
                opportunity.WorkspaceId == workspaceId && opportunity.Id == payment.OpportunityId &&
                !opportunity.IsDeleted && opportunity.OwnerUserId == actorId));
        decimal? collectedThisMonth = null;
        int? paidThisMonth = null;
        if (canSeeValues)
        {
            var paid = await paymentQuery.Where(x => x.PaidAtUtc >= monthStartUtc && x.PaidAtUtc < nextMonthStartUtc &&
                    (x.Status == nameof(PaymentStatus.Paid) || x.Status == nameof(PaymentStatus.Refunded)))
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            var refunded = await paymentQuery.Where(x => x.RefundedAtUtc >= monthStartUtc && x.RefundedAtUtc < nextMonthStartUtc)
                .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
            collectedThisMonth = paid - refunded;
            paidThisMonth = await paymentQuery.CountAsync(x => x.Status == nameof(PaymentStatus.Paid) &&
                x.PaidAtUtc >= monthStartUtc && x.PaidAtUtc < nextMonthStartUtc, cancellationToken);
        }

        var myAvailabilityRow = await db.EmployeeAvailabilities.AsNoTracking()
            .SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == actorId, cancellationToken);
        string? myAvailability = myAvailabilityRow?.Status.ToString() ?? EmployeeAvailabilityStatus.Available.ToString();
        var availability = new List<DashboardAvailabilityMetric>();
        int? availableEmployees = null, busyEmployees = null, onLeaveEmployees = null, offlineEmployees = null;
        if (companyWide)
        {
            var groups = await db.EmployeeAvailabilities.AsNoTracking().Where(x => x.WorkspaceId == workspaceId)
                .GroupBy(x => x.Status).Select(group => new { Status = group.Key, Count = group.Count() }).ToListAsync(cancellationToken);
            var counts = Enum.GetValues<EmployeeAvailabilityStatus>().ToDictionary(x => x, _ => 0);
            foreach (var item in groups) counts[item.Status] = item.Count;
            availableEmployees = counts[EmployeeAvailabilityStatus.Available];
            busyEmployees = counts[EmployeeAvailabilityStatus.Busy];
            onLeaveEmployees = counts[EmployeeAvailabilityStatus.OnLeave];
            offlineEmployees = counts[EmployeeAvailabilityStatus.Offline];
            availability.AddRange(counts.Select(x => new DashboardAvailabilityMetric(x.Key.ToString(), x.Value)));
        }

        var upcomingRows = new List<CalendarAppointment>();
        if (Crm.Can(PermissionCatalog.CalendarUse))
        {
            var appointmentQuery = db.CalendarAppointments.AsNoTracking().Where(x => x.WorkspaceId == workspaceId &&
                x.Status == nameof(AppointmentStatus.Scheduled) && x.StartsAtUtc >= now);
            if (!companyWide) appointmentQuery = appointmentQuery.Where(x => x.CreatedByUserId == actorId);
            upcomingRows = await appointmentQuery.OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).Take(5).ToListAsync(cancellationToken);
        }
        var contactIds = upcomingRows.Where(x => x.ContactId.HasValue).Select(x => x.ContactId!.Value).Distinct().ToArray();
        var creatorIds = upcomingRows.Select(x => x.CreatedByUserId).Distinct().ToArray();
        var contactNames = await db.Contacts.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var creatorNames = await db.Users.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && creatorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var appointments = upcomingRows.Select(x => new DashboardAppointmentMetric(x.Id, x.Title,
            x.ContactId.HasValue ? contactNames.GetValueOrDefault(x.ContactId.Value) : null,
            DateTime.SpecifyKind(x.StartsAtUtc, DateTimeKind.Utc), x.TimeZoneId,
            creatorNames.GetValueOrDefault(x.CreatedByUserId, "Workspace member"))).ToArray();

        return Ok(new DashboardOverviewResponse(DateTime.SpecifyKind(now, DateTimeKind.Utc), companyWide,
            newLeadsToday, openLeads, averageResponseSeconds, recoveryCount, escalations,
            availableEmployees, busyEmployees, onLeaveEmployees, offlineEmployees, myAvailability,
            openPipelineValue, !canSeeValues, collectedThisMonth, paidThisMonth, !canSeeValues,
            stageMetrics, availability, appointments));
    }
}
