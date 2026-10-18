using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace leadflow_crm.API.Controllers;

[ApiController]
[Route("api/public/leads")]
[AllowAnonymous]
[EnableRateLimiting("public-lead-intake")]
public sealed class PublicLeadIntakeController(LeadWorkflowService workflow) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<IntakeQueuedResponse>> Create(
        [FromHeader(Name = "X-Leadflow-Intake-Key")] string publicKey,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        LeadIntakeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await workflow.CreatePublicIntakeAsync(publicKey, idempotencyKey, request, cancellationToken);
            return StatusCode(result.IsDuplicate ? StatusCodes.Status200OK :
                result.Status == "Assigned" ? StatusCodes.Status201Created : StatusCodes.Status202Accepted, result);
        }
        catch (CrmException exception)
        {
            return Problem(statusCode: exception.Status, title: "Public lead intake failed", detail: exception.Message);
        }
    }
}
