using Microsoft.EntityFrameworkCore;
using SQLInventory.Data;
using SQLInventory.Data.Entities;

namespace SQLInventory.Services;

public class DatabaseService(AppDbContext db)
{
    public Task<List<SqlDatabase>> GetAllAsync(int? instanceId = null)
    {
        var q = db.SqlDatabases.Include(d => d.Instance).AsQueryable();
        if (instanceId.HasValue)
            q = q.Where(d => d.InstanceId == instanceId.Value);
        return q.OrderBy(d => d.Instance.ServerName).ThenBy(d => d.DatabaseName).ToListAsync();
    }

    public Task<SqlDatabase?> GetByIdAsync(int id) =>
        db.SqlDatabases.Include(d => d.Instance).FirstOrDefaultAsync(d => d.DatabaseId == id);

    public async Task<SqlDatabase> CreateAsync(SqlDatabase database)
    {
        database.CreatedUtc = DateTime.UtcNow;
        database.ModifiedUtc = DateTime.UtcNow;
        db.SqlDatabases.Add(database);
        await db.SaveChangesAsync();
        return database;
    }

    public async Task UpdateAsync(SqlDatabase database)
    {
        database.ModifiedUtc = DateTime.UtcNow;
        db.SqlDatabases.Update(database);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var db2 = await db.SqlDatabases.FindAsync(id);
        if (db2 is not null)
        {
            db.SqlDatabases.Remove(db2);
            await db.SaveChangesAsync();
        }
    }
}
