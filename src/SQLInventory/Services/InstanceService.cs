using Microsoft.EntityFrameworkCore;
using SQLInventory.Data;
using SQLInventory.Data.Entities;
using Environment = SQLInventory.Data.Entities.Environment;

namespace SQLInventory.Services;

public class InstanceService(AppDbContext db)
{
    public Task<List<SqlInstance>> GetAllAsync(string? search = null, int? environmentId = null, bool? isActive = null)
    {
        var q = db.SqlInstances
            .Include(i => i.Environment)
            .Include(i => i.InstanceTags).ThenInclude(it => it.Tag)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(i => i.ServerName.Contains(search) ||
                              (i.InstanceName != null && i.InstanceName.Contains(search)) ||
                              (i.Notes != null && i.Notes.Contains(search)));

        if (environmentId.HasValue)
            q = q.Where(i => i.EnvironmentId == environmentId.Value);

        if (isActive.HasValue)
            q = q.Where(i => i.IsActive == isActive.Value);

        return q.OrderBy(i => i.ServerName).ThenBy(i => i.InstanceName).ToListAsync();
    }

    public Task<SqlInstance?> GetByIdAsync(int id) =>
        db.SqlInstances
          .Include(i => i.Environment)
          .Include(i => i.InstanceTags).ThenInclude(it => it.Tag)
          .Include(i => i.Databases)
          .Include(i => i.PrimaryAvailabilityGroups).ThenInclude(ag => ag.Replicas).ThenInclude(r => r.Instance)
          .Include(i => i.AgReplicas).ThenInclude(r => r.AvailabilityGroup)
          .FirstOrDefaultAsync(i => i.InstanceId == id);

    public async Task<SqlInstance> CreateAsync(SqlInstance instance)
    {
        instance.CreatedUtc = DateTime.UtcNow;
        instance.ModifiedUtc = DateTime.UtcNow;
        db.SqlInstances.Add(instance);
        await db.SaveChangesAsync();
        return instance;
    }

    public async Task UpdateAsync(SqlInstance instance)
    {
        instance.ModifiedUtc = DateTime.UtcNow;
        db.SqlInstances.Update(instance);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var instance = await db.SqlInstances.FindAsync(id);
        if (instance is not null)
        {
            db.SqlInstances.Remove(instance);
            await db.SaveChangesAsync();
        }
    }

    public async Task SetTagsAsync(int instanceId, IEnumerable<int> tagIds)
    {
        var existing = await db.InstanceTags.Where(it => it.InstanceId == instanceId).ToListAsync();
        db.InstanceTags.RemoveRange(existing);
        foreach (var tagId in tagIds)
            db.InstanceTags.Add(new InstanceTag { InstanceId = instanceId, TagId = tagId });
        await db.SaveChangesAsync();
    }

    public Task<List<Environment>> GetEnvironmentsAsync() =>
        db.Environments.OrderBy(e => e.Name).ToListAsync();

    public async Task<DashboardStats> GetDashboardStatsAsync()
    {
        var instanceCount = await db.SqlInstances.CountAsync(i => i.IsActive);
        var databaseCount = await db.SqlDatabases.CountAsync();
        var agCount = await db.AvailabilityGroups.CountAsync();
        var envBreakdown = await db.SqlInstances
            .Include(i => i.Environment)
            .Where(i => i.IsActive)
            .GroupBy(i => new { i.Environment.Name, i.Environment.ColorHex })
            .Select(g => new EnvironmentCount(g.Key.Name, g.Key.ColorHex, g.Count()))
            .ToListAsync();
        var recent = await db.SqlInstances
            .Include(i => i.Environment)
            .OrderByDescending(i => i.ModifiedUtc)
            .Take(5)
            .ToListAsync();

        return new DashboardStats(instanceCount, databaseCount, agCount, envBreakdown, recent);
    }
}

public record EnvironmentCount(string Name, string ColorHex, int Count);
public record DashboardStats(
    int ActiveInstances,
    int TotalDatabases,
    int AvailabilityGroups,
    List<EnvironmentCount> ByEnvironment,
    List<SqlInstance> RecentlyModified);
