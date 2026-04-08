namespace SQLInventory.Data.Entities;

public class AvailabilityGroup
{
    public int AgId { get; set; }
    public int PrimaryInstanceId { get; set; }
    public string AgName { get; set; } = "";
    public string? ClusterType { get; set; }
    public string? AutomatedBackupPreference { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }

    public SqlInstance PrimaryInstance { get; set; } = null!;
    public ICollection<AgReplica> Replicas { get; set; } = [];
}
