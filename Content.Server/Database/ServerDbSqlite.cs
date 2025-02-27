using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.IP;
using Content.Server.Preferences;
using Content.Server.Preferences.Managers;
using Content.Shared;
using Content.Shared.CCVar;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Network;


namespace Content.Server.Database
{
    /// <summary>
    ///     Provides methods to retrieve and update character preferences.
    ///     Don't use this directly, go through <see cref="ServerPreferencesManager" /> instead.
    /// </summary>
    public sealed class ServerDbSqlite : ServerDbBase
    {
        // For SQLite we use a single DB context via SQLite.
        // This doesn't allow concurrent access so that's what the semaphore is for.
        // That said, this is bloody SQLite, I don't even think EFCore bothers to truly async it.
        private readonly SemaphoreSlim _prefsSemaphore = new(1, 1);

        private readonly Task _dbReadyTask;
        private readonly SqliteServerDbContext _prefsCtx;

        public ServerDbSqlite(DbContextOptions<ServerDbContext> options)
        {
            _prefsCtx = new SqliteServerDbContext(options);

            if (IoCManager.Resolve<IConfigurationManager>().GetCVar(CCVars.DatabaseSynchronous))
            {
                _prefsCtx.Database.Migrate();
                _dbReadyTask = Task.CompletedTask;
            }
            else
            {
                _dbReadyTask = Task.Run(() => _prefsCtx.Database.Migrate());
            }
        }

        public override async Task<ServerBanDef?> GetServerBanAsync(int id)
        {
            await using var db = await GetDbImpl();

            var ban = await db.SqliteDbContext.Ban
                .Include(p => p.Unban)
                .Where(p => p.Id == id)
                .SingleOrDefaultAsync();

            return ConvertBan(ban);
        }

        public override async Task<ServerBanDef?> GetServerBanAsync(
            IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId)
        {
            await using var db = await GetDbImpl();

            // SQLite can't do the net masking stuff we need to match IP address ranges.
            // So just pull down the whole list into memory.
            var bans = await db.SqliteDbContext.Ban
                .Include(p => p.Unban)
                .Where(p => p.Unban == null && (p.ExpirationTime == null || p.ExpirationTime.Value > DateTime.UtcNow))
                .ToListAsync();

            return bans.FirstOrDefault(b => BanMatches(b, address, userId, hwId)) is { } foundBan
                ? ConvertBan(foundBan)
                : null;
        }

        public override async Task<List<ServerBanDef>> GetServerBansAsync(
            IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId)
        {
            await using var db = await GetDbImpl();

            // SQLite can't do the net masking stuff we need to match IP address ranges.
            // So just pull down the whole list into memory.
            var queryBans = await db.SqliteDbContext.Ban
                .Include(p => p.Unban)
                .ToListAsync();

            return queryBans
                .Where(b => BanMatches(b, address, userId, hwId))
                .Select(ConvertBan)
                .ToList()!;
        }

        private static bool BanMatches(
            SqliteServerBan ban,
            IPAddress? address,
            NetUserId? userId,
            ImmutableArray<byte>? hwId)
        {
            if (address != null && ban.Address is not null && IPAddressExt.IsInSubnet(address, ban.Address.Value))
            {
                return true;
            }

            if (userId is { } id && ban.UserId == id.UserId)
            {
                return true;
            }

            if (hwId is { } hwIdVar && hwIdVar.AsSpan().SequenceEqual(ban.HWId))
            {
                return true;
            }

            return false;
        }

        public override async Task AddServerBanAsync(ServerBanDef serverBan)
        {
            await using var db = await GetDbImpl();

            db.SqliteDbContext.Ban.Add(new SqliteServerBan
            {
                Address = serverBan.Address,
                Reason = serverBan.Reason,
                BanningAdmin = serverBan.BanningAdmin?.UserId,
                HWId = serverBan.HWId?.ToArray(),
                BanTime = serverBan.BanTime.UtcDateTime,
                ExpirationTime = serverBan.ExpirationTime?.UtcDateTime,
                UserId = serverBan.UserId?.UserId
            });

            await db.SqliteDbContext.SaveChangesAsync();
        }

        public override async Task AddServerUnbanAsync(ServerUnbanDef serverUnban)
        {
            await using var db = await GetDbImpl();

            db.SqliteDbContext.Unban.Add(new SqliteServerUnban
            {
                BanId = serverUnban.BanId,
                UnbanningAdmin = serverUnban.UnbanningAdmin?.UserId,
                UnbanTime = serverUnban.UnbanTime.UtcDateTime
            });

            await db.SqliteDbContext.SaveChangesAsync();
        }

        public override async Task UpdatePlayerRecord(
            NetUserId userId,
            string userName,
            IPAddress address,
            ImmutableArray<byte> hwId)
        {
            await using var db = await GetDbImpl();

            var record = await db.SqliteDbContext.Player.SingleOrDefaultAsync(p => p.UserId == userId.UserId);
            if (record == null)
            {
                db.SqliteDbContext.Player.Add(record = new SqlitePlayer
                {
                    FirstSeenTime = DateTime.UtcNow,
                    UserId = userId.UserId,
                });
            }

            record.LastSeenTime = DateTime.UtcNow;
            record.LastSeenAddress = address.ToString();
            record.LastSeenUserName = userName;
            record.LastSeenHWId = hwId.ToArray();

            await db.SqliteDbContext.SaveChangesAsync();
        }

        public override async Task<PlayerRecord?> GetPlayerRecordByUserName(string userName, CancellationToken cancel)
        {
            await using var db = await GetDbImpl();

            // Sort by descending last seen time.
            // So if due to account renames we have two people with the same username in the DB,
            // the most recent one is picked.
            var record = await db.SqliteDbContext.Player
                .OrderByDescending(p => p.LastSeenTime)
                .FirstOrDefaultAsync(p => p.LastSeenUserName == userName, cancel);

            return MakePlayerRecord(record);
        }

        public override async Task<PlayerRecord?> GetPlayerRecordByUserId(NetUserId userId, CancellationToken cancel)
        {
            await using var db = await GetDbImpl();

            var record = await db.SqliteDbContext.Player
                .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);

            return MakePlayerRecord(record);
        }

        private static PlayerRecord? MakePlayerRecord(SqlitePlayer? record)
        {
            if (record == null)
            {
                return null;
            }

            return new PlayerRecord(
                new NetUserId(record.UserId),
                new DateTimeOffset(record.FirstSeenTime, TimeSpan.Zero),
                record.LastSeenUserName,
                new DateTimeOffset(record.LastSeenTime, TimeSpan.Zero),
                IPAddress.Parse(record.LastSeenAddress),
                record.LastSeenHWId?.ToImmutableArray());
        }

        private static ServerBanDef? ConvertBan(SqliteServerBan? ban)
        {
            if (ban == null)
            {
                return null;
            }

            NetUserId? uid = null;
            if (ban.UserId is { } guid)
            {
                uid = new NetUserId(guid);
            }

            NetUserId? aUid = null;
            if (ban.BanningAdmin is { } aGuid)
            {
                aUid = new NetUserId(aGuid);
            }

            var unban = ConvertUnban(ban.Unban);

            return new ServerBanDef(
                ban.Id,
                uid,
                ban.Address,
                ban.HWId == null ? null : ImmutableArray.Create(ban.HWId),
                ban.BanTime,
                ban.ExpirationTime,
                ban.Reason,
                aUid,
                unban);
        }

        private static ServerUnbanDef? ConvertUnban(SqliteServerUnban? unban)
        {
            if (unban == null)
            {
                return null;
            }

            NetUserId? aUid = null;
            if (unban.UnbanningAdmin is { } aGuid)
            {
                aUid = new NetUserId(aGuid);
            }

            return new ServerUnbanDef(
                unban.Id,
                aUid,
                unban.UnbanTime);
        }

        public override async Task AddConnectionLogAsync(NetUserId userId, string userName, IPAddress address,
            ImmutableArray<byte> hwId)
        {
            await using var db = await GetDbImpl();

            db.SqliteDbContext.ConnectionLog.Add(new SqliteConnectionLog
            {
                Address = address.ToString(),
                Time = DateTime.UtcNow,
                UserId = userId.UserId,
                UserName = userName,
                HWId = hwId.ToArray()
            });

            await db.SqliteDbContext.SaveChangesAsync();
        }

        public override async Task<((Admin, string? lastUserName)[] admins, AdminRank[])> GetAllAdminAndRanksAsync(
            CancellationToken cancel)
        {
            await using var db = await GetDbImpl();

            var admins = await db.SqliteDbContext.Admin
                .Include(a => a.Flags)
                .GroupJoin(db.SqliteDbContext.Player, a => a.UserId, p => p.UserId, (a, grouping) => new {a, grouping})
                .SelectMany(t => t.grouping.DefaultIfEmpty(), (t, p) => new {t.a, p!.LastSeenUserName})
                .ToArrayAsync(cancel);

            var adminRanks = await db.DbContext.AdminRank.Include(a => a.Flags).ToArrayAsync(cancel);

            return (admins.Select(p => (p.a, p.LastSeenUserName)).ToArray(), adminRanks)!;
        }

        private async Task<DbGuardImpl> GetDbImpl()
        {
            await _dbReadyTask;
            await _prefsSemaphore.WaitAsync();

            return new DbGuardImpl(this);
        }

        protected override async Task<DbGuard> GetDb()
        {
            return await GetDbImpl();
        }

        private sealed class DbGuardImpl : DbGuard
        {
            private readonly ServerDbSqlite _db;

            public DbGuardImpl(ServerDbSqlite db)
            {
                _db = db;
            }

            public override ServerDbContext DbContext => _db._prefsCtx;
            public SqliteServerDbContext SqliteDbContext => _db._prefsCtx;

            public override ValueTask DisposeAsync()
            {
                _db._prefsSemaphore.Release();
                return default;
            }
        }
    }
}
