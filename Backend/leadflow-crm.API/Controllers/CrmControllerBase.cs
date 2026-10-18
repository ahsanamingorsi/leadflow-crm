using System.Data;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
public abstract class CrmControllerBase(CrmService crm) : Controller
{
    protected CrmService Crm => crm;

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        try
        {
            await crm.InitializeAsync(User);
            var write = !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method);
            // Configuration checks and record writes share a transaction, including deletes and stage moves.
            await using var transaction = write
                ? await crm.Db.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
            var executed = await next();
            if (executed.Exception is not null && !executed.ExceptionHandled)
            {
                var problem = MapError(executed.Exception);
                if (problem is null) return;
                executed.ExceptionHandled = true;
                executed.Result = problem;
                return;
            }
            if (write && executed.Exception is null && executed.Result is not ObjectResult { StatusCode: >= 400 })
                await transaction!.CommitAsync();
        }
        catch (Exception exception) when (MapError(exception) is not null)
        {
            context.Result = MapError(exception);
        }
    }

    private static ObjectResult? MapError(Exception exception)
    {
        // EF's execution strategy can wrap a deadlock in both InvalidOperationException
        // and DbUpdateException. Inspect the chain so callers still receive a conflict.
        var sql = FindCause<SqlException>(exception);
        var (status, detail) = exception switch
        {
            CrmException error => (error.Status, error.Message),
            DbUpdateConcurrencyException => (409, "This record changed. Reload it before saving again."),
            _ when sql?.Number is 2601 or 2627 => (409, "A record with this name already exists in this scope."),
            _ when sql?.Number == 547 => (409, "Related records prevent this change."),
            _ when sql?.Number == 1205 => (409, "Another operation changed related data. Reload and try again."),
            _ => (0, "")
        };
        return status == 0 ? null : new ObjectResult(new ProblemDetails
        { Status = status, Title = status == 409 ? "Conflict" : "Request could not be completed", Detail = detail }) { StatusCode = status };
    }

    private static T? FindCause<T>(Exception exception) where T : Exception
    {
        for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
            if (cause is T match) return match;
        return null;
    }
}
