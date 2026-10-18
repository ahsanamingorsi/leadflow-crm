using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Data;

public sealed class LeadflowDbContext(DbContextOptions<LeadflowDbContext> options) : DbContext(options)
{
}
