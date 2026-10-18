namespace leadflow_crm.API.Models;

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "PKR";
    public string TimeZoneId { get; set; } = "Asia/Karachi";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    public ICollection<ApplicationRole> Roles { get; set; } = new List<ApplicationRole>();
}
