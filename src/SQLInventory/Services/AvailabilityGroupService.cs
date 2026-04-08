using Microsoft.EntityFrameworkCore;
using SQLInventory.Data;
using SQLInventory.Data.Entities;

namespace SQLInventory.Services;

public class AvailabilityGroupService(AppDbContext db)
{
    public Task<List<AvailabilityGroup>> GetAllAsync() =>
        db.AvailabilityGroups
          .Include(ag => ag.PrimaryInstance).ThenInclude(i => i.Environment)
          .Include(ag => ag.Replicas).ThenInclude(r => r.Instance)
          .OrderBy(ag => ag.AgName)
          .ToListAsync();

    public Task<AvailabilityGroup?> GetByIdAsync(int id) =>
        db.AvailabilityGroups
          .Include(ag => ag.PrimaryInstance).ThenInclude(i => i.Environment)
          .Include(ag => ag.Replicas).ThenInclude(r => r.Instance)
          .FirstOrDefaultAsync(ag => ag.AgId == id);

    public async Task<AvailabilityGroup> CreateAsync(AvailabilityGroup ag)
    {
        ag.CreatedUtc = DateTime.UtcNow;
        ag.ModifiedUtc = DateTime.UtcNow;
        db.AvailabilityGroups.Add(ag);
        await db.SaveChangesAsync();
        return ag;
    }

    public async Task UpdateAsync(AvailabilityGroup ag)
    {
        ag.ModifiedUtc = DateTime.UtcNow;
        db.AvailabilityGroups.Update(ag);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var ag = await db.AvailabilityGroups.FindAsync(id);
        if (ag is not null)
        {
            db.AvailabilityGroups.Remove(ag);
            await db.SaveChangesAsync();
        }
    }

    public async Task AddReplicaAsync(AgReplica replica)
    {
        db.AgReplicas.Add(replica);
        await db.SaveChangesAsync();
    }

    public async Task RemoveReplicaAsync(int replicaId)
    {
        var replica = await db.AgReplicas.FindAsync(replicaId);
        if (replica is not null)
        {
            db.AgReplicas.Remove(replica);
            await db.SaveChangesAsync();
        }
    }
}
