using Microsoft.EntityFrameworkCore;
using SQLInventory.Data;
using SQLInventory.Data.Entities;

namespace SQLInventory.Services;

public class TagService(AppDbContext db)
{
    public Task<List<Tag>> GetAllAsync() =>
        db.Tags.OrderBy(t => t.Name).ToListAsync();

    public Task<Tag?> GetByIdAsync(int id) =>
        db.Tags.FindAsync(id).AsTask();

    public async Task<Tag> CreateAsync(Tag tag)
    {
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return tag;
    }

    public async Task UpdateAsync(Tag tag)
    {
        db.Tags.Update(tag);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var tag = await db.Tags.FindAsync(id);
        if (tag is not null)
        {
            db.Tags.Remove(tag);
            await db.SaveChangesAsync();
        }
    }
}
