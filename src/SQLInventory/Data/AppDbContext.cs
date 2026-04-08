using Microsoft.EntityFrameworkCore;
using SQLInventory.Data.Entities;
using Environment = SQLInventory.Data.Entities.Environment;

namespace SQLInventory.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Environment> Environments => Set<Environment>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<SqlInstance> SqlInstances => Set<SqlInstance>();
    public DbSet<InstanceTag> InstanceTags => Set<InstanceTag>();
    public DbSet<SqlDatabase> SqlDatabases => Set<SqlDatabase>();
    public DbSet<AvailabilityGroup> AvailabilityGroups => Set<AvailabilityGroup>();
    public DbSet<AgReplica> AgReplicas => Set<AgReplica>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Environment
        modelBuilder.Entity<Environment>(e =>
        {
            e.ToTable("Environments");
            e.HasKey(x => x.EnvironmentId);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(7).HasDefaultValue("#6c757d");
            e.HasIndex(x => x.Name).IsUnique();
        });

        // Tag
        modelBuilder.Entity<Tag>(e =>
        {
            e.ToTable("Tags");
            e.HasKey(x => x.TagId);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ColorHex).HasMaxLength(7).HasDefaultValue("#0d6efd");
            e.HasIndex(x => x.Name).IsUnique();
        });

        // SqlInstance
        modelBuilder.Entity<SqlInstance>(e =>
        {
            e.ToTable("SqlInstances");
            e.HasKey(x => x.InstanceId);
            e.Property(x => x.ServerName).HasMaxLength(255).IsRequired();
            e.Property(x => x.InstanceName).HasMaxLength(128);
            e.Property(x => x.Port).HasDefaultValue(1433);
            e.Property(x => x.SqlVersion).HasMaxLength(50);
            e.Property(x => x.SqlEdition).HasMaxLength(100);
            e.Property(x => x.SqlBuild).HasMaxLength(50);
            e.Property(x => x.HostOperatingSystem).HasMaxLength(255);
            e.Property(x => x.ServiceAccount).HasMaxLength(255);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.ModifiedUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => new { x.ServerName, x.InstanceName }).IsUnique();
            e.Ignore(x => x.FullName);
            e.Ignore(x => x.ConnectionName);

            e.HasOne(x => x.Environment)
             .WithMany(x => x.Instances)
             .HasForeignKey(x => x.EnvironmentId);
        });

        // InstanceTag (many-to-many join)
        modelBuilder.Entity<InstanceTag>(e =>
        {
            e.ToTable("InstanceTags");
            e.HasKey(x => new { x.InstanceId, x.TagId });

            e.HasOne(x => x.Instance)
             .WithMany(x => x.InstanceTags)
             .HasForeignKey(x => x.InstanceId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Tag)
             .WithMany(x => x.InstanceTags)
             .HasForeignKey(x => x.TagId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // SqlDatabase
        modelBuilder.Entity<SqlDatabase>(e =>
        {
            e.ToTable("SqlDatabases");
            e.HasKey(x => x.DatabaseId);
            e.Property(x => x.DatabaseName).HasMaxLength(128).IsRequired();
            e.Property(x => x.SizeMb).HasColumnType("decimal(18,2)");
            e.Property(x => x.RecoveryModel).HasMaxLength(20);
            e.Property(x => x.StateDesc).HasMaxLength(60);
            e.Property(x => x.Owner).HasMaxLength(128);
            e.Property(x => x.CollationName).HasMaxLength(128);
            e.Property(x => x.CreatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.ModifiedUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.HasIndex(x => new { x.InstanceId, x.DatabaseName }).IsUnique();
            e.Ignore(x => x.SizeGb);

            e.HasOne(x => x.Instance)
             .WithMany(x => x.Databases)
             .HasForeignKey(x => x.InstanceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // AvailabilityGroup
        modelBuilder.Entity<AvailabilityGroup>(e =>
        {
            e.ToTable("AvailabilityGroups");
            e.HasKey(x => x.AgId);
            e.Property(x => x.AgName).HasMaxLength(128).IsRequired();
            e.Property(x => x.ClusterType).HasMaxLength(50);
            e.Property(x => x.AutomatedBackupPreference).HasMaxLength(50);
            e.Property(x => x.CreatedUtc).HasDefaultValueSql("SYSUTCDATETIME()");
            e.Property(x => x.ModifiedUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasOne(x => x.PrimaryInstance)
             .WithMany(x => x.PrimaryAvailabilityGroups)
             .HasForeignKey(x => x.PrimaryInstanceId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // AgReplica
        modelBuilder.Entity<AgReplica>(e =>
        {
            e.ToTable("AgReplicas");
            e.HasKey(x => x.ReplicaId);
            e.Property(x => x.Role).HasMaxLength(20).IsRequired();
            e.Property(x => x.AvailabilityMode).HasMaxLength(30);
            e.Property(x => x.FailoverMode).HasMaxLength(20);
            e.Property(x => x.SeedingMode).HasMaxLength(20);
            e.HasIndex(x => new { x.AgId, x.InstanceId }).IsUnique();

            e.HasOne(x => x.AvailabilityGroup)
             .WithMany(x => x.Replicas)
             .HasForeignKey(x => x.AgId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Instance)
             .WithMany(x => x.AgReplicas)
             .HasForeignKey(x => x.InstanceId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
