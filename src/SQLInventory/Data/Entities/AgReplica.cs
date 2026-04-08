namespace SQLInventory.Data.Entities;

public class AgReplica
{
    public int ReplicaId { get; set; }
    public int AgId { get; set; }
    public int InstanceId { get; set; }
    public string Role { get; set; } = "";
    public string? AvailabilityMode { get; set; }
    public string? FailoverMode { get; set; }
    public string? SeedingMode { get; set; }

    public AvailabilityGroup AvailabilityGroup { get; set; } = null!;
    public SqlInstance Instance { get; set; } = null!;
}
