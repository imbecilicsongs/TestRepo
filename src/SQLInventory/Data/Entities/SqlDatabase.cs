namespace SQLInventory.Data.Entities;

public class SqlDatabase
{
    public int DatabaseId { get; set; }
    public int InstanceId { get; set; }
    public string DatabaseName { get; set; } = "";
    public decimal? SizeMb { get; set; }
    public int? CompatibilityLevel { get; set; }
    public string? RecoveryModel { get; set; }
    public string? StateDesc { get; set; }
    public bool IsReadOnly { get; set; }
    public string? Owner { get; set; }
    public string? CollationName { get; set; }
    public DateTime? LastFullBackupUtc { get; set; }
    public DateTime? LastLogBackupUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public SqlInstance Instance { get; set; } = null!;

    public decimal? SizeGb => SizeMb.HasValue ? Math.Round(SizeMb.Value / 1024, 2) : null;
}
