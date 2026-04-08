namespace SQLInventory.Data.Entities;

public class Environment
{
    public int EnvironmentId { get; set; }
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#6c757d";

    public ICollection<SqlInstance> Instances { get; set; } = [];
}
