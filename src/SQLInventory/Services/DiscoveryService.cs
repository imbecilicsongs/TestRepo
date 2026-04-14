using Microsoft.Data.SqlClient;
using SQLInventory.Data.Entities;

namespace SQLInventory.Services;

public class DiscoveryResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public SqlInstance? Instance { get; set; }
    public List<SqlDatabase> Databases { get; set; } = [];
    public List<DiscoveredAg> AvailabilityGroups { get; set; } = [];
}

public class DiscoveredAg
{
    public string AgName { get; set; } = "";
    public string? ClusterType { get; set; }
    public string? AutomatedBackupPreference { get; set; }
    public List<DiscoveredReplica> Replicas { get; set; } = [];
}

public class DiscoveredReplica
{
    public string ReplicaServerName { get; set; } = "";
    public string Role { get; set; } = "";
    public string? AvailabilityMode { get; set; }
    public string? FailoverMode { get; set; }
    public string? SeedingMode { get; set; }
}

public class DiscoveryService(InstanceService instanceService, DatabaseService databaseService)
{
    public async Task<DiscoveryResult> DiscoverAsync(string serverName, string? instanceName, int port,
        string? username, string? password, int environmentId, CancellationToken ct = default)
    {
        var fullName = instanceName is not null ? $"{serverName}\\{instanceName}" : serverName;
        var connTarget = port != 1433 ? $"{fullName},{port}" : fullName;

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = connTarget,
            InitialCatalog = "master",
            TrustServerCertificate = true,
            ConnectTimeout = 15
        };

        if (!string.IsNullOrWhiteSpace(username))
        {
            builder.UserID = username;
            builder.Password = password;
            builder.IntegratedSecurity = false;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        var result = new DiscoveryResult();

        try
        {
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync(ct);

            var instance = new SqlInstance
            {
                ServerName = serverName,
                InstanceName = string.IsNullOrWhiteSpace(instanceName) ? null : instanceName,
                Port = port,
                EnvironmentId = environmentId,
                LastDiscoveredUtc = DateTime.UtcNow
            };

            // ── Instance metadata ───────────────────────────────────────────
            const string instanceSql = """
                SELECT
                    @@VERSION                                          AS FullVersion,
                    CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR) AS Build,
                    CAST(SERVERPROPERTY('Edition')        AS NVARCHAR) AS Edition,
                    CAST(SERVERPROPERTY('IsClustered')    AS INT)      AS IsClustered,
                    CAST(SERVERPROPERTY('IsHadrEnabled')  AS INT)      AS IsAlwaysOnEnabled,
                    CAST(SERVERPROPERTY('ComputerNamePhysicalNetBIOS') AS NVARCHAR) AS HostName;
                """;

            await using (var cmd = new SqlCommand(instanceSql, conn))
            await using (var rdr = await cmd.ExecuteReaderAsync(ct))
            {
                if (await rdr.ReadAsync(ct))
                {
                    var fullVer = rdr["FullVersion"]?.ToString() ?? "";
                    instance.SqlBuild = rdr["Build"]?.ToString();
                    instance.SqlEdition = rdr["Edition"]?.ToString();
                    instance.IsClustered = Convert.ToInt32(rdr["IsClustered"]) == 1;
                    instance.IsAlwaysOnEnabled = Convert.ToInt32(rdr["IsAlwaysOnEnabled"]) == 1;
                    instance.HostOperatingSystem = rdr["HostName"]?.ToString();

                    // Parse major version year from build (e.g., 15.x = 2019, 16.x = 2022)
                    if (instance.SqlBuild is not null && instance.SqlBuild.Split('.') is [var major, ..])
                    {
                        instance.SqlVersion = major switch
                        {
                            "16" => "2022",
                            "15" => "2019",
                            "14" => "2017",
                            "13" => "2016",
                            "12" => "2014",
                            "11" => "2012",
                            _ => major
                        };
                    }
                    _ = fullVer; // used for debugging if needed
                }
            }

            // ── CPU / Memory ────────────────────────────────────────────────
            const string sysSql = "SELECT cpu_count, physical_memory_kb / 1024 AS PhysicalMemoryMb FROM sys.dm_os_sys_info;";
            await using (var cmd = new SqlCommand(sysSql, conn))
            await using (var rdr = await cmd.ExecuteReaderAsync(ct))
            {
                if (await rdr.ReadAsync(ct))
                {
                    instance.CpuCount = rdr.GetInt32(rdr.GetOrdinal("cpu_count"));
                }
            }

            // ── Max memory config ───────────────────────────────────────────
            const string memSql = "SELECT CAST(value_in_use AS INT) AS MaxMemoryMb FROM sys.configurations WHERE name = 'max server memory (MB)';";
            await using (var cmd = new SqlCommand(memSql, conn))
            await using (var rdr = await cmd.ExecuteReaderAsync(ct))
            {
                if (await rdr.ReadAsync(ct))
                    instance.MaxMemoryMb = rdr.GetInt32(0);
            }

            result.Instance = instance;

            // ── Databases ───────────────────────────────────────────────────
            const string dbSql = """
                SELECT
                    d.name,
                    d.state_desc,
                    d.recovery_model_desc,
                    d.compatibility_level,
                    d.collation_name,
                    SUSER_SNAME(d.owner_sid) AS owner,
                    d.create_date,
                    CAST(SUM(mf.size) * 8.0 / 1024 AS DECIMAL(18,2)) AS SizeMb
                FROM sys.databases d
                JOIN sys.master_files mf ON d.database_id = mf.database_id
                WHERE d.database_id > 4
                GROUP BY d.name, d.state_desc, d.recovery_model_desc, d.compatibility_level,
                         d.collation_name, d.owner_sid, d.create_date;
                """;

            await using (var cmd = new SqlCommand(dbSql, conn))
            await using (var rdr = await cmd.ExecuteReaderAsync(ct))
            {
                while (await rdr.ReadAsync(ct))
                {
                    result.Databases.Add(new SqlDatabase
                    {
                        DatabaseName = rdr["name"].ToString()!,
                        StateDesc = rdr["state_desc"]?.ToString(),
                        RecoveryModel = rdr["recovery_model_desc"]?.ToString(),
                        CompatibilityLevel = rdr["compatibility_level"] as int?,
                        CollationName = rdr["collation_name"]?.ToString(),
                        Owner = rdr["owner"]?.ToString(),
                        SizeMb = rdr["SizeMb"] as decimal?
                    });
                }
            }

            // ── Last backup dates ───────────────────────────────────────────
            const string backupSql = """
                SELECT database_name, type, MAX(backup_finish_date) AS LastBackup
                FROM msdb.dbo.backupset
                WHERE database_name IN (SELECT name FROM sys.databases WHERE database_id > 4)
                GROUP BY database_name, type;
                """;

            try
            {
                await using var cmd = new SqlCommand(backupSql, conn);
                await using var rdr = await cmd.ExecuteReaderAsync(ct);
                while (await rdr.ReadAsync(ct))
                {
                    var dbName = rdr["database_name"].ToString();
                    var type = rdr["type"].ToString();
                    var lastBackup = rdr["LastBackup"] as DateTime?;
                    var match = result.Databases.FirstOrDefault(d => d.DatabaseName == dbName);
                    if (match is null) continue;
                    if (type == "D") match.LastFullBackupUtc = lastBackup;
                    else if (type == "L") match.LastLogBackupUtc = lastBackup;
                }
            }
            catch
            {
                // msdb may not be accessible; skip backup history
            }

            // ── Availability Groups ─────────────────────────────────────────
            if (instance.IsAlwaysOnEnabled)
            {
                const string agSql = """
                    SELECT
                        ag.name                              AS AgName,
                        ag.cluster_type_desc                 AS ClusterType,
                        ag.automated_backup_preference_desc  AS BackupPref,
                        ar.replica_server_name               AS ReplicaServer,
                        ar.availability_mode_desc            AS AvailMode,
                        ar.failover_mode_desc                AS FailoverMode,
                        ar.seeding_mode_desc                 AS SeedingMode,
                        ars.role_desc                        AS Role
                    FROM sys.availability_groups ag
                    JOIN sys.availability_replicas ar
                        ON ag.group_id = ar.group_id
                    JOIN sys.dm_hadr_availability_replica_states ars
                        ON ar.replica_id = ars.replica_id;
                    """;

                try
                {
                    await using var cmd = new SqlCommand(agSql, conn);
                    await using var rdr = await cmd.ExecuteReaderAsync(ct);
                    while (await rdr.ReadAsync(ct))
                    {
                        var agName = rdr["AgName"].ToString()!;
                        var ag = result.AvailabilityGroups.FirstOrDefault(a => a.AgName == agName);
                        if (ag is null)
                        {
                            ag = new DiscoveredAg
                            {
                                AgName = agName,
                                ClusterType = rdr["ClusterType"]?.ToString(),
                                AutomatedBackupPreference = rdr["BackupPref"]?.ToString()
                            };
                            result.AvailabilityGroups.Add(ag);
                        }
                        ag.Replicas.Add(new DiscoveredReplica
                        {
                            ReplicaServerName = rdr["ReplicaServer"].ToString()!,
                            Role = rdr["Role"].ToString()!,
                            AvailabilityMode = rdr["AvailMode"]?.ToString(),
                            FailoverMode = rdr["FailoverMode"]?.ToString(),
                            SeedingMode = rdr["SeedingMode"]?.ToString()
                        });
                    }
                }
                catch
                {
                    // AG views may not be accessible on secondary; skip
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task<SqlInstance> ImportAsync(DiscoveryResult result, int environmentId)
    {
        if (!result.Success || result.Instance is null)
            throw new InvalidOperationException("Discovery did not succeed.");

        var instance = result.Instance;

        // Upsert instance
        var existing = (await instanceService.GetAllAsync())
            .FirstOrDefault(i =>
                string.Equals(i.ServerName, instance.ServerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.InstanceName, instance.InstanceName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            await instanceService.CreateAsync(instance);
        }
        else
        {
            existing.SqlVersion = instance.SqlVersion;
            existing.SqlEdition = instance.SqlEdition;
            existing.SqlBuild = instance.SqlBuild;
            existing.HostOperatingSystem = instance.HostOperatingSystem;
            existing.IsClustered = instance.IsClustered;
            existing.IsAlwaysOnEnabled = instance.IsAlwaysOnEnabled;
            existing.MaxMemoryMb = instance.MaxMemoryMb;
            existing.CpuCount = instance.CpuCount;
            existing.LastDiscoveredUtc = instance.LastDiscoveredUtc;
            await instanceService.UpdateAsync(existing);
            instance = existing;
        }

        // Upsert databases
        var existingDbs = await databaseService.GetAllAsync(instance.InstanceId);
        foreach (var db in result.Databases)
        {
            db.InstanceId = instance.InstanceId;
            var existingDb = existingDbs.FirstOrDefault(d =>
                string.Equals(d.DatabaseName, db.DatabaseName, StringComparison.OrdinalIgnoreCase));

            if (existingDb is null)
                await databaseService.CreateAsync(db);
            else
            {
                existingDb.SizeMb = db.SizeMb;
                existingDb.StateDesc = db.StateDesc;
                existingDb.RecoveryModel = db.RecoveryModel;
                existingDb.CompatibilityLevel = db.CompatibilityLevel;
                existingDb.CollationName = db.CollationName;
                existingDb.Owner = db.Owner;
                existingDb.LastFullBackupUtc = db.LastFullBackupUtc;
                existingDb.LastLogBackupUtc = db.LastLogBackupUtc;
                await databaseService.UpdateAsync(existingDb);
            }
        }

        return instance;
    }
}
