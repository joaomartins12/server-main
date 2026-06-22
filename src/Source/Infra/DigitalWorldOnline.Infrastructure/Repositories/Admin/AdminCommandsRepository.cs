using AutoMapper;
using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Commons.DTOs.Account;
using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Commons.DTOs.Character;
using DigitalWorldOnline.Commons.DTOs.Config;
using DigitalWorldOnline.Commons.DTOs.Config.Events;
using DigitalWorldOnline.Commons.DTOs.Digimon;
using DigitalWorldOnline.Commons.DTOs.Server;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Models.Account;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace DigitalWorldOnline.Infrastructure.Repositories.Admin
{
    public class AdminCommandsRepository : IAdminCommandsRepository
    {
        private readonly DatabaseContext _context;
        private readonly IMapper _mapper;

        public AdminCommandsRepository(
            DatabaseContext context,
            IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<AccountDTO> AddAccountAsync(AccountDTO account)
        {
            // Garante que o DiscordId está salvo corretamente e não duplicado
            if (!string.IsNullOrEmpty(account.DiscordId))
            {
                var exists = await _context.Account
                    .AsNoTracking()
                    .AnyAsync(x => x.DiscordId == account.DiscordId);

                if (exists)
                    throw new InvalidOperationException("Já existe uma conta com este DiscordId.");
            }

            _context.Account.Add(account);
            await _context.SaveChangesAsync();

            return account;
        }

        public async Task<SummonDTO> AddSummonConfigAsync(SummonDTO summon)
        {
            _context.SummonsConfig.Add(summon);
            await _context.SaveChangesAsync();
            return summon;
        }

        public async Task<bool> UpdateGotchaAssetAsync(GotchaAssetDTO machine)
        {
            try
            {
                var entity = await _context.GotchaAsset
                    .AsSplitQuery()
                    .Include(g => g.Items)
                    .Include(g => g.RareItems)
                    .FirstOrDefaultAsync(g => g.GotchaId == machine.GotchaId);

                if (entity == null)
                    return false;

                // Atualiza propriedades básicas
                entity.NpcId = machine.NpcId;
                entity.UseItem = machine.UseItem;
                entity.UseCount = machine.UseCount;
                entity.Chance = machine.Chance;
                entity.MinLv = machine.MinLv;
                entity.MaxLv = machine.MaxLv;
                entity.RareItemCnt = machine.RareItemCnt;
                entity.Active = machine.Active;

                // Remove itens existentes antes de adicionar os novos
                _context.RemoveRange(entity.Items);
                _context.RemoveRange(entity.RareItems);

                // Adiciona novos itens normais
                entity.Items = machine.Items.Select(i => new GotchaItemsAssetDTO
                {
                    ItemId = i.ItemId,
                    ItemCount = i.ItemCount,
                    InitialQuanty = i.InitialQuanty,
                    Quanty = i.Quanty,
                    Name = i.Name,
                    GotchaId = entity.GotchaId
                }).ToList();

                // Adiciona novos itens raros
                entity.RareItems = machine.RareItems.Select(r => new GotchaRareItemsAssetDTO
                {
                    RareItem = r.RareItem,
                    RareItemCnt = r.RareItemCnt,
                    RareItemGive = r.RareItemGive,
                    Name = r.Name,
                    GotchaId = entity.GotchaId
                }).ToList();

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating GotchaAsset: {ex.Message}");
                return false;
            }
        }
        public async Task<ContainerAssetDTO> AddContainerConfigAsync(ContainerAssetDTO container)
        {
            _context.Container.Add(container);
            await _context.SaveChangesAsync();
            return container;
        }

        public async Task<MobConfigDTO> AddMobAsync(MobConfigDTO mob)
        {
            var targetMap = await _context.MapConfig
                .AsNoTracking()
                .SingleAsync(x => x.Id == mob.GameMapConfigId);

            mob.Location.MapId = (short)targetMap.MapId;
            mob.DropReward?.Drops.ForEach(drop => drop.Id = 0);

            _context.MobConfig.Add(mob);
            await _context.SaveChangesAsync();

            return mob;
        }

        public async Task<SummonMobDTO> AddSummonMobAsync(SummonMobDTO mob)
        {
            var targetSummon = await _context.SummonsConfig
                .AsNoTracking()
                .SingleAsync(x => x.Id == mob.SummonDTOId);

            // Usa o primeiro Map do SummonConfig ou 0 se não existir
            mob.Location.MapId = (short)targetSummon.Maps.FirstOrDefault();

            // Reset Drop IDs para evitar conflitos
            mob.DropReward?.Drops.ForEach(drop => drop.Id = 0);

            _context.SummonsMobConfig.Add(mob);
            await _context.SaveChangesAsync();

            return mob;
        }

        public async Task<ScanDetailAssetDTO> AddScanConfigAsync(ScanDetailAssetDTO scan)
        {
            _context.ScanDetail.Add(scan);
            await _context.SaveChangesAsync();
            return scan;
        }

        public async Task<ServerDTO> AddServerAsync(ServerDTO server)
        {
            _context.ServerConfig.Add(server);
            await _context.SaveChangesAsync();
            return server;
        }

        public async Task<MapRegionAssetDTO> AddSpawnPointAsync(MapRegionAssetDTO spawnPoint, int mapId)
        {
            var mapRegionList = await _context.MapRegionListAsset
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Regions)
                .SingleOrDefaultAsync(x => x.MapId == mapId);

            if (mapRegionList != null)
            {
                spawnPoint.MapRegionListId = mapRegionList.Id;
                mapRegionList.Regions.Add(spawnPoint);

                _context.Update(mapRegionList);
                _context.MapRegionAsset.Add(spawnPoint);

                await _context.SaveChangesAsync();
            }

            return spawnPoint;
        }
        public async Task<UserDTO> AddUserAsync(UserDTO user)
        {
            _context.UserConfig.Add(user);
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task DeleteAccountAsync(long id)
        {
            var dto = await _context.Account
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.RemoveRange(
                    await _context.Character
                        .Where(x => x.AccountId == id)
                        .ToListAsync()
                );

                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteSummonAsync(long id)
        {
            var dto = await _context.SummonsConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteContainerConfigAsync(long id)
        {
            var dto = await _context.Container
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Rewards)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteMapMobsAsync(long id)
        {
            var dto = await _context.MapConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Mobs)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.RemoveRange(dto.Mobs);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteMobAsync(long id)
        {
            var dto = await _context.MobConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteSummonMobAsync(long id)
        {
            var dto = await _context.SummonsMobConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteScanConfigAsync(long id)
        {
            var dto = await _context.ScanDetail
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Rewards)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteServerAsync(long id)
        {
            var dto = await _context.ServerConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteSpawnPointAsync(long id)
        {
            var dto = await _context.MapRegionAsset
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteUserAsync(long id)
        {
            var dto = await _context.UserConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DuplicateMobAsync(long id)
        {
            var dto = await _context.MobConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward).ThenInclude(y => y.Drops)
                .Include(x => x.DropReward).ThenInclude(y => y.BitsDrop)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                var clonedEntity = (MobConfigDTO)dto.Clone();
                clonedEntity.Id = 0;

                if (clonedEntity.Location == null)
                    clonedEntity.Location = new MobLocationConfigDTO();
                else
                    clonedEntity.Location.Id = 0;

                if (clonedEntity.ExpReward == null)
                    clonedEntity.ExpReward = new MobExpRewardConfigDTO();
                else
                    clonedEntity.ExpReward.Id = 0;

                if (clonedEntity.DropReward == null)
                    clonedEntity.DropReward = new MobDropRewardConfigDTO();
                else
                {
                    clonedEntity.DropReward.Id = 0;
                    clonedEntity.DropReward.Drops.ForEach(drop => drop.Id = 0);
                    clonedEntity.DropReward.BitsDrop.Id = 0;
                }

                _context.Add(clonedEntity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DuplicateSummonMobAsync(long id)
        {
            var dto = await _context.SummonsMobConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward).ThenInclude(y => y.Drops)
                .Include(x => x.DropReward).ThenInclude(y => y.BitsDrop)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                var clonedEntity = (SummonMobDTO)dto.Clone();
                clonedEntity.Id = 0;

                if (clonedEntity.Location == null)
                    clonedEntity.Location = new SummonMobLocationDTO();
                else
                    clonedEntity.Location.Id = 0;

                if (clonedEntity.ExpReward == null)
                    clonedEntity.ExpReward = new SummonMobExpRewardDTO();
                else
                    clonedEntity.ExpReward.Id = 0;

                if (clonedEntity.DropReward == null)
                    clonedEntity.DropReward = new SummonMobDropRewardDTO();
                else
                {
                    clonedEntity.DropReward.Id = 0;
                    clonedEntity.DropReward.Drops.ForEach(drop => drop.Id = 0);
                    clonedEntity.DropReward.BitsDrop.Id = 0;
                }

                _context.Add(clonedEntity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateAccountAsync(AccountDTO account)
        {
            var entity = await _context.Account
                .SingleOrDefaultAsync(x => x.Id == account.Id);

            if (entity != null)
            {
                entity.Username = account.Username;
                entity.Email = account.Email;
                entity.Premium = account.Premium;
                entity.Silk = account.Silk;
                entity.AccessLevel = account.AccessLevel;

                // Só atualiza a senha se for fornecida
                if (!string.IsNullOrWhiteSpace(account.Password))
                {
                    entity.Password = account.Password;
                }

                entity.DiscordId = account.DiscordId;

                await _context.SaveChangesAsync();
            }
        }

        public async Task<AccountBlockDTO> AddAccountBlockAsync(AccountBlockDTO accountBlock)
        {
            _context.AccountBlock.Add(accountBlock);
            await _context.SaveChangesAsync();
            return accountBlock;
        }

        public async Task UpdateScanConfigAsync(ScanDetailAssetDTO scan)
        {
            var dto = await _context.ScanDetail
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Rewards)
                .SingleOrDefaultAsync(x => x.Id == scan.Id);

            if (dto == null)
            {
                _context.Add(scan);
            }
            else
            {
                var parameterIds = scan.Rewards.Select(x => x.Id);
                var removeItems = dto.Rewards.Where(x => !parameterIds.Contains(x.Id));
                foreach (var removeItem in removeItems)
                {
                    _context.Remove(removeItem);
                }

                var databaseIds = dto.Rewards.Select(x => x.Id);
                var newItems = scan.Rewards.Where(x => !databaseIds.Contains(x.Id));
                foreach (var newItem in newItems)
                {
                    newItem.Id = 0;
                    newItem.ScanDetailAssetId = dto.Id;

                    _context.Add(newItem);
                }

                dto.Rewards = scan.Rewards;
                dto.ItemId = scan.ItemId;
                dto.ItemName = scan.ItemName;
                dto.MinAmount = scan.MinAmount;
                dto.MaxAmount = scan.MaxAmount;

                _context.Update(dto);
            }

            await _context.SaveChangesAsync();
        }

        public async Task UpdateContainerConfigAsync(ContainerAssetDTO container)
        {
            var dto = await _context.Container
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Rewards)
                .SingleOrDefaultAsync(x => x.Id == container.Id);

            if (dto == null)
            {
                _context.Add(container);
            }
            else
            {
                var parameterIds = container.Rewards.Select(x => x.Id);
                var removeItems = dto.Rewards.Where(x => !parameterIds.Contains(x.Id));
                foreach (var removeItem in removeItems)
                {
                    _context.Remove(removeItem);
                }

                var databaseIds = dto.Rewards.Select(x => x.Id);
                var newItems = container.Rewards.Where(x => !databaseIds.Contains(x.Id));
                foreach (var newItem in newItems)
                {
                    newItem.Id = 0;
                    newItem.ContainerAssetId = dto.Id;

                    _context.Add(newItem);
                }

                dto.Rewards = container.Rewards;
                dto.ItemId = container.ItemId;
                dto.ItemName = container.ItemName;
                dto.RewardAmount = container.RewardAmount;

                _context.Update(dto);
            }

            await _context.SaveChangesAsync();
        }

        public async Task UpdateServerAsync(ServerDTO server)
        {
            var dto = await _context.ServerConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == server.Id);

            if (dto != null)
            {
                dto.Name = server.Name;
                dto.Experience = server.Experience;
                dto.Maintenance = server.Maintenance;
                dto.New = dto.CreateDate.AddDays(7) >= DateTime.Now;
                dto.Type = server.Type;
                dto.Port = server.Port;

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateSpawnPointAsync(MapRegionAssetDTO spawnPoint, long mapId)
        {
            var dto = await _context.MapRegionAsset
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == spawnPoint.Id);

            if (dto != null)
            {
                dto.X = spawnPoint.X;
                dto.Y = spawnPoint.Y;
                dto.Index = spawnPoint.Index;
                dto.Name = spawnPoint.Name;

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateUserAsync(UserDTO user)
        {
            var dto = await _context.UserConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == user.Id);

            if (dto != null)
            {
                dto.Username = user.Username;
                dto.AccessLevel = user.AccessLevel;

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateAccessAsync(UserDTO user)
        {
            var dto = await _context.Account
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == user.Id);

            if (dto != null)
            {
                dto.AccessLevel = (AccountAccessLevelEnum)user.AccessLevel;

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteCloneConfigAsync(long id)
        {
            var dto = await _context.CloneConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<CloneConfigDTO> AddCloneConfigAsync(CloneConfigDTO clone)
        {
            _context.CloneConfig.Add(clone);
            await _context.SaveChangesAsync();
            return clone;
        }

        public async Task UpdateCloneConfigAsync(CloneConfigDTO clone)
        {
            var dto = await _context.CloneConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == clone.Id);

            if (dto == null)
            {
                _context.Add(clone);
            }
            else
            {
                dto.Type = clone.Type;
                dto.Level = clone.Level;
                dto.SuccessChance = clone.SuccessChance;
                dto.BreakChance = clone.BreakChance;
                dto.MinAmount = clone.MinAmount;
                dto.MaxAmount = clone.MaxAmount;

                _context.Update(dto);
            }

            await _context.SaveChangesAsync();
        }

        public async Task<GlobalDropsConfigDTO> AddGlobalDropsConfigAsync(GlobalDropsConfigDTO globalDrops)
        {
            _context.GlobalDropsConfig.Add(globalDrops);
            await _context.SaveChangesAsync();
            return globalDrops;
        }

        public async Task DeleteGlobalDropsConfigAsync(long id)
        {
            var dto = await _context.GlobalDropsConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> UpdatePlayerQuestsAsync(long characterId, short questId, bool isCompleted)
        {
            var sql = $@"
DECLARE @CharacterId BIGINT = {characterId};
DECLARE @QuestId INT = {questId};
DECLARE @BlockIndex INT = (@QuestId - 1) / 32;
DECLARE @BitIndex INT = (@QuestId - 1) % 32;

-- Atualizar CompletedDataValue
;WITH BitSplit AS (
    SELECT 
        value,
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS idx
    FROM STRING_SPLIT(
        (SELECT CompletedDataValue FROM [DTU].[Character].[Progress] WHERE CharacterId = @CharacterId), ';'
    )
), UpdatedBits AS (
    SELECT 
        idx,
        CASE 
            WHEN idx = @BlockIndex THEN 
                CAST(
                    CASE 
                        WHEN {Convert.ToInt32(isCompleted)} = 1
                        THEN CAST(value AS INT) | POWER(2, @BitIndex)       -- marcar como completa (SET bit)
                        ELSE CAST(value AS INT) & ~POWER(2, @BitIndex)      -- marcar como incompleta (CLEAR bit)
                    END AS VARCHAR
                )
            ELSE value
        END AS value
    FROM BitSplit
)
UPDATE P
SET CompletedDataValue = (
    SELECT STRING_AGG(value, ';') FROM UpdatedBits
)
FROM [DTU].[Character].[Progress] P
WHERE CharacterId = @CharacterId;

-- Atualizar CompletedData (em blocos de 8 quests)
DECLARE @ByteIndex INT = @QuestId / 8;

;WITH ByteSplit AS (
    SELECT 
        value,
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS idx
    FROM STRING_SPLIT(
        (SELECT CompletedData FROM [DTU].[Character].[Progress] WHERE CharacterId = @CharacterId), ';'
    )
), UpdatedBytes AS (
    SELECT 
        idx,
        CASE 
            WHEN idx = @ByteIndex THEN 
                CASE 
                    WHEN {Convert.ToInt32(isCompleted)} = 1
                    THEN '1'
                    ELSE '0'
                END
            ELSE value
        END AS value
    FROM ByteSplit
)
UPDATE P
SET CompletedData = (
    SELECT STRING_AGG(value, ';') FROM UpdatedBytes
)
FROM [DTU].[Character].[Progress] P
WHERE CharacterId = @CharacterId;
";

            await _context.Database.ExecuteSqlRawAsync(sql);
            return true;
        }


        public async Task<bool> DeletePlayerActiveQuestsAsync(long characterId)
        {
            var questIds = new List<int>();

            // 1. Recuperar os QuestIds ativos
            var questIdSql = $@"
SELECT QuestId
FROM [DTU].[Character].[InProgressQuest]
WHERE [CharacterProgressId] IN (
    SELECT [Id]
    FROM [DTU].[Character].[Progress]
    WHERE [CharacterId] = {characterId}
);";

            var connection = _context.Database.GetDbConnection();
            try
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = questIdSql;

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            questIds.Add(Convert.ToInt32(reader.GetValue(0)));
                        }
                    }
                }
            }
            finally
            {
                await connection.CloseAsync();
            }

            // 2. Resetar quests ativas
            if (questIds.Any())
            {
                var questIdList = string.Join(",", questIds);

                var resetSql = $@"
DECLARE @CharacterId BIGINT = {characterId};
DECLARE @DailyQuestIds TABLE (QuestId INT);

INSERT INTO @DailyQuestIds (QuestId)
SELECT value FROM STRING_SPLIT('{questIdList}', ',');

-- Reset CompletedDataValue
WITH BitSplit AS (
    SELECT 
        value,
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS idx
    FROM STRING_SPLIT(
        (SELECT CompletedDataValue FROM [DTU].[Character].[Progress] WHERE CharacterId = @CharacterId), ';'
    )
), ResetBits AS (
    SELECT 
        idx,
        CASE 
            WHEN EXISTS (
                SELECT 1 FROM @DailyQuestIds d 
                WHERE d.QuestId BETWEEN (idx * 32 + 1) AND ((idx + 1) * 32)
            )
            THEN CAST(
                CAST(value AS BIGINT) & ~(
                    SELECT ISNULL(SUM(POWER(CAST(2 AS BIGINT), (d.QuestId - 1) % 32)), 0)
                    FROM @DailyQuestIds d
                    WHERE d.QuestId BETWEEN (idx * 32 + 1) AND ((idx + 1) * 32)
                ) AS VARCHAR)
            ELSE value
        END AS value
    FROM BitSplit
)
UPDATE P
SET CompletedDataValue = (
    SELECT STRING_AGG(value, ';') FROM ResetBits
)
FROM [DTU].[Character].[Progress] P
WHERE CharacterId = @CharacterId;

-- Reset CompletedData
WITH ByteSplit AS (
    SELECT 
        value,
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS idx
    FROM STRING_SPLIT(
        (SELECT CompletedData FROM [DTU].[Character].[Progress] WHERE CharacterId = @CharacterId), ';'
    )
), ResetBytes AS (
    SELECT 
        idx,
        CASE 
            WHEN EXISTS (
                SELECT 1 FROM @DailyQuestIds d 
                WHERE d.QuestId BETWEEN idx AND idx + 2
            )
            THEN '0'
            ELSE value
        END AS value
    FROM ByteSplit
)
UPDATE P
SET CompletedData = (
    SELECT STRING_AGG(value, ';') FROM ResetBytes
)
FROM [DTU].[Character].[Progress] P
WHERE CharacterId = @CharacterId;
";

                await _context.Database.ExecuteSqlRawAsync(resetSql);
            }

            // 3. Deletar as quests ativas
            var deleteSql = $@"
DELETE FROM [DTU].[Character].[InProgressQuest]
WHERE [CharacterProgressId] IN (
    SELECT [Id]
    FROM [DTU].[Character].[Progress]
    WHERE [CharacterId] = {characterId}
);";

            await _context.Database.ExecuteSqlRawAsync(deleteSql);

            return true;
        }




        public async Task UpdateGlobalDropsConfigAsync(GlobalDropsConfigDTO globalDrops)
        {
            var dto = await _context.GlobalDropsConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == globalDrops.Id);

            if (dto != null)
            {
                dto.ItemId = globalDrops.ItemId;
                dto.MinDrop = globalDrops.MinDrop;
                dto.MaxDrop = globalDrops.MaxDrop;
                dto.Chance = globalDrops.Chance;
                dto.Map = globalDrops.Map;
                dto.StartTime = globalDrops.StartTime;
                dto.EndTime = globalDrops.EndTime;

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<HatchConfigDTO> AddHatchConfigAsync(HatchConfigDTO hatch)
        {
            _context.HatchConfig.Add(hatch);
            await _context.SaveChangesAsync();
            return hatch;
        }

        public async Task DeleteHatchConfigAsync(long id)
        {
            var dto = await _context.HatchConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateHatchConfigAsync(HatchConfigDTO hatch)
        {
            var dto = await _context.HatchConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == hatch.Id);

            if (dto != null)
            {
                dto.Type = hatch.Type;
                dto.SuccessChance = hatch.SuccessChance;
                dto.BreakChance = hatch.BreakChance;

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<AccountCreateResult> CreateAccountAsync(string username, string email, string discordId,
    string password)
        {
            var existentAccount = await _context.Account
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.Username == username ||
                    x.Email == email ||
                    x.DiscordId == discordId);

            if (existentAccount != null)
            {
                if (existentAccount.Username == username)
                    return AccountCreateResult.UsernameInUse;

                if (existentAccount.Email == email)
                    return AccountCreateResult.EmailInUse;

                if (existentAccount.DiscordId == discordId)
                    return AccountCreateResult.DiscordInUse;
            }

            var dto = _mapper.Map<AccountDTO>(AccountModel.Create(username, email, discordId, password));

            _context.Add(dto);
            await _context.SaveChangesAsync();

            return AccountCreateResult.Created;
        }

        public async Task<EventConfigDTO> AddEventConfigAsync(EventConfigDTO eventConfig)
        {
            _context.EventConfig.Add(eventConfig);
            await _context.SaveChangesAsync();
            return eventConfig;
        }

        public async Task DeleteEventConfigAsync(long id)
        {
            var dto = await _context.EventConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateEventConfigAsync(EventConfigDTO eventConfig)
        {
            var dto = await _context.EventConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == eventConfig.Id);

            if (dto != null)
            {
                dto.Name = eventConfig.Name;
                dto.Description = eventConfig.Description;
                dto.IsEnabled = eventConfig.IsEnabled;
                dto.StartDay = eventConfig.StartDay;
                dto.StartsAt = eventConfig.StartsAt;
                dto.Rounds = eventConfig.Rounds;

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<EventMapsConfigDTO> AddEventMapConfigAsync(EventMapsConfigDTO eventMapConfig)
        {
            _context.EventMapsConfig.Add(eventMapConfig);
            await _context.SaveChangesAsync();
            return eventMapConfig;
        }

        public async Task DeleteEventMapConfigAsync(long id)
        {
            var dto = await _context.EventMapsConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateEventMapConfigAsync(EventMapsConfigDTO eventMapConfig)
        {
            var dto = await _context.EventMapsConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == eventMapConfig.Id);

            if (dto != null)
            {
                dto.MapId = eventMapConfig.MapId;
                dto.Channels = eventMapConfig.Channels;
                dto.IsEnabled = eventMapConfig.IsEnabled;

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<EventMobConfigDTO> AddEventMobAsync(EventMobConfigDTO mob)
        {
            var targetMap = await _context.EventMapsConfig
                .AsNoTracking()
                .SingleAsync(x => x.Id == mob.EventMapConfigId);

            mob.Location.MapId = (short)targetMap.MapId;
            mob.DropReward?.Drops.ForEach(drop => drop.Id = 0);

            _context.EventMobConfig.Add(mob);
            await _context.SaveChangesAsync();

            return mob;
        }

        public async Task RemoveAccountBlockAsync(long accountId)
        {
            var block = await _context.AccountBlock
                .Where(b => b.AccountId == accountId)
                .OrderByDescending(b => b.StartDate) // pega o ban mais recente
                .FirstOrDefaultAsync();

            if (block != null)
            {
                _context.AccountBlock.Remove(block);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteEventMapMobsAsync(long id)
        {
            var dto = await _context.EventMapsConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Mobs)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.RemoveRange(dto.Mobs);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteEventMobAsync(long id)
        {
            var dto = await _context.EventMobConfig
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DuplicateEventMobAsync(long id)
        {
            var dto = await _context.EventMobConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward).ThenInclude(y => y.Drops)
                .Include(x => x.DropReward).ThenInclude(y => y.BitsDrop)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                var clonedEntity = (EventMobConfigDTO)dto.Clone();
                clonedEntity.Id = 0;

                if (clonedEntity.Location == null)
                    clonedEntity.Location = new EventMobLocationConfigDTO();
                else
                    clonedEntity.Location.Id = 0;

                if (clonedEntity.ExpReward == null)
                    clonedEntity.ExpReward = new EventMobExpRewardConfigDTO();
                else
                    clonedEntity.ExpReward.Id = 0;

                if (clonedEntity.DropReward == null)
                    clonedEntity.DropReward = new EventMobDropRewardConfigDTO();
                else
                {
                    clonedEntity.DropReward.Id = 0;
                    clonedEntity.DropReward.Drops.ForEach(drop => drop.Id = 0);
                    clonedEntity.DropReward.BitsDrop.Id = 0;
                }

                _context.Add(clonedEntity);
                await _context.SaveChangesAsync();
            }
        }


        public async Task<bool> UpdatePlayerAsync(
    long id,
    string name,
    byte level,
    long currentExperience,
    int mapId,
    CharacterStateEnum state,
    CharacterEventStateEnum eventState,
    byte channel,
    CharacterModelEnum model,
    short size,
    int currentHp,
    int currentDs,
    int xGauge,
    short xCrystals,
    short currentTitle,
    byte digimonSlots,
    List<DigimonDTO> updatedDigimons)
        {
            var character = await _context.Character
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.Xai)
                .Include(x => x.Digimons)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (character == null)
                return false;

            // Atualiza dados do personagem
            character.Name = name;
            character.Level = level;
            character.CurrentExperience = currentExperience;
            character.State = state;
            character.EventState = eventState;
            character.Channel = channel;
            character.Model = model;
            character.Size = size;
            character.CurrentHp = currentHp;
            character.CurrentDs = currentDs;
            character.CurrentTitle = currentTitle;
            character.DigimonSlots = digimonSlots;

            if (character.Location != null)
                character.Location.MapId = (short)mapId;

            if (character.Xai != null)
            {
                character.Xai.XGauge = xGauge;
                character.Xai.XCrystals = xCrystals;
            }

            // Sincroniza Digimons
            if (updatedDigimons != null)
            {
                var existingDigimons = await _context.Digimon
                    .Where(d => d.CharacterId == id)
                    .ToListAsync();

                // Remove digimons que foram excluídos
                foreach (var existing in existingDigimons)
                {
                    if (!updatedDigimons.Any(d => d.Id == existing.Id))
                    {
                        _context.Digimon.Remove(existing);
                    }
                }

                // Atualiza ou adiciona digimons
                foreach (var digimonDto in updatedDigimons)
                {
                    digimonDto.CharacterId = id;

                    if (digimonDto.Id == 0)
                    {
                        await _context.Digimon.AddAsync(digimonDto);
                    }
                    else
                    {
                        var tracked = existingDigimons.FirstOrDefault(d => d.Id == digimonDto.Id);
                        if (tracked != null)
                        {
                            // Atualiza campos manualmente
                            tracked.Name = digimonDto.Name;
                            tracked.Level = digimonDto.Level;
                            tracked.CurrentExperience = digimonDto.CurrentExperience;
                            tracked.CurrentHp = digimonDto.CurrentHp;
                            tracked.CurrentDs = digimonDto.CurrentDs;
                            tracked.HatchGrade = digimonDto.HatchGrade;
                            tracked.Slot = digimonDto.Slot;
                            tracked.Digiclone = digimonDto.Digiclone;
                            tracked.CurrentType = digimonDto.CurrentType;
                            // tracked.EvolutionStage = digimonDto.EvolutionStage;
                            // tracked.IsDeleted = digimonDto.IsDeleted;
                            // ... outros campos se necessário
                        }
                    }
                }
            }

            _context.Update(character);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}