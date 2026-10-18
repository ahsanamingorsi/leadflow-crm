using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;

namespace leadflow_crm.API.Services;

public static class CrmQueries
{
    public static IQueryable<T> ByCreatedDate<T>(IQueryable<T> query, RecordQuery request) where T : CrmRecord
    {
        if (request.CreatedFromUtc > request.CreatedToUtc) throw new CrmException(400, "The start date must be before the end date.");
        if (request.CreatedFromUtc.HasValue) query = query.Where(x => x.CreatedAtUtc >= request.CreatedFromUtc.Value);
        if (request.CreatedToUtc.HasValue) query = query.Where(x => x.CreatedAtUtc < request.CreatedToUtc.Value);
        return query;
    }
}
