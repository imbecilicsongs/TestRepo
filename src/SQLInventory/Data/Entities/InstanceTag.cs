namespace SQLInventory.Data.Entities;

public class InstanceTag
{
    public int InstanceId { get; set; }
    public int TagId { get; set; }

    public SqlInstance Instance { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
