using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace leadflow_crm.API.Controllers;

[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
public abstract class IntegrationApiControllerBase(CrmService crm) : Controller
{
    protected CrmService Crm => crm;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            await crm.InitializeAsync(User);
            var executed = await next();
            if (executed.Exception is not null && !executed.ExceptionHandled)
            {
                var result = MapError(executed.Exception);
                if (result is null) return;
                executed.ExceptionHandled = true;
                executed.Result = result;
            }
        }
        catch (Exception exception)
        {
            var result = MapError(exception);
            if (result is null) throw;
            context.Result = result;
        }
    }

    private static ObjectResult? MapError(Exception exception)
    {
        var cause = exception;
        while (cause is not null)
        {
            if (cause is CrmException crmException)
                return new ObjectResult(new ProblemDetails
                {
                    Status = crmException.Status,
                    Title = crmException.Status == 403 ? "Forbidden" : "Request could not be completed",
                    Detail = crmException.Message
                }) { StatusCode = crmException.Status };
            cause = cause.InnerException!;
        }
        return null;
    }
}
