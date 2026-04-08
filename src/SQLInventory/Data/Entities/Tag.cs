namespace SQLInventory.Data.Entities;

public class Tag
{
    public int TagId { get; set; }
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#0d6efd";

    public ICollection<InstanceTag> InstanceTags { get; set; } = [];
}
