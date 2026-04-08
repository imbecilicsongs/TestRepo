namespace SQLInventory.Data.Entities;

public class SqlInstance
{
    public int InstanceId { get; set; }
    public string ServerName { get; set; } = "";
    public string? InstanceName { get; set; }
    public int Port { get; set; } = 1433;
    public int EnvironmentId { get; set; }
    public string? SqlVersion { get; set; }
    public string? SqlEdition { get; set; }
    public string? SqlBuild { get; set; }
    public string? HostOperatingSystem { get; set; }
    public bool IsClustered { get; set; }
    public bool IsAlwaysOnEnabled { get; set; }
    public int? MaxMemoryMb { get; set; }
    public int? CpuCount { get; set; }
    public string? ServiceAccount { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastDiscoveredUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public Environment Environment { get; set; } = null!;
    public ICollection<SqlDatabase> Databases { get; set; } = [];
    public ICollection<InstanceTag> InstanceTags { get; set; } = [];
    public ICollection<AvailabilityGroup> PrimaryAvailabilityGroups { get; set; } = [];
    public ICollection<AgReplica> AgReplicas { get; set; } = [];

    public string FullName => InstanceName is not null
        ? $"{ServerName}\\{InstanceName}"
        : ServerName;

    public string ConnectionName => Port != 1433
        ? $"{FullName},{Port}"
        : FullName;
}
