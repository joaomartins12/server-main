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
                    .AnyAsync(x => x.DiscordId == account.DiscordId);

                if (exists)
                    throw new InvalidOperationException("Já existe uma conta com este DiscordId.");
            }

            await _context.Account.AddAsync(account);
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
                    .Include(g => g.Items)
                    .Include(g => g.RareItems)
                    .FirstOrDefaultAsync(g => g.GotchaId == machine.GotchaId);

                if (entity == null) return false;

                // Atualiza propriedades básicas
                entity.NpcId = machine.NpcId;
                entity.UseItem = machine.UseItem;
                entity.UseCount = machine.UseCount;
                entity.Chance = machine.Chance;
                entity.MinLv = machine.MinLv;
                entity.MaxLv = machine.MaxLv;
                entity.RareItemCnt = machine.RareItemCnt;

                // Se você alterou o DTO para usar bool:
                entity.Active = machine.Active;

                // Se NÃO alterou o DTO e está usando a propriedade intermediária:
                // entity.Active = machine.Active; // Mantém como está se for int
                // Ou se estiver usando a propriedade IsActive:
                // entity.Active = isActive ? 1 : 0;

                // Remove itens existentes
                _context.RemoveRange(entity.Items);
                _context.RemoveRange(entity.RareItems);

                // Adiciona novos itens normais com todos os campos
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
                // Log do erro
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
                .SingleAsync(x => x.Id == mob.SummonDTOId);

            // Assign the first map from the SummonConfig or default to 0 if none exist
            mob.Location.MapId = (short)targetSummon.Maps.FirstOrDefault();

            // Reset Drop IDs to prevent conflicts
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
                .Include(x => x.Regions)
                .SingleOrDefaultAsync(x => x.MapId == mapId);

            if (mapRegionList == null)
                return null;

            spawnPoint.MapRegionListId = mapRegionList.Id;

            // Adiciona o spawnPoint diretamente
            await _context.MapRegionAsset.AddAsync(spawnPoint);

            // Também adiciona à lista em memória (caso seja necessário para lógica interna)
            mapRegionList.Regions.Add(spawnPoint);

            await _context.SaveChangesAsync();

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
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            var characters = await _context.Character
                .Where(x => x.AccountId == id)
                .ToListAsync();

            if (characters.Count > 0)
                _context.RemoveRange(characters);

            _context.Remove(dto);

            await _context.SaveChangesAsync();
        }

        public async Task DeleteSummonAsync(long id)
        {
            var dto = await _context.SummonsConfig
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            _context.Remove(dto);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteContainerConfigAsync(long id)
        {
            var dto = await _context.Container
                .Include(x => x.Rewards)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            _context.Remove(dto);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteMapMobsAsync(long id)
        {
            var dto = await _context.MapConfig
                .Include(x => x.Mobs)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            if (dto.Mobs != null && dto.Mobs.Count > 0)
            {
                _context.RemoveRange(dto.Mobs);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteMobAsync(long id)
        {
            var dto = await _context.MobConfig
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            _context.Remove(dto);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteSummonMobAsync(long id)
        {
            var dto = await _context.SummonsMobConfig
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            _context.Remove(dto);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteScanConfigAsync(long id)
        {
            var dto = await _context.ScanDetail
                .Include(x => x.Rewards)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            _context.Remove(dto);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteServerAsync(long id)
        {
            var dto = await _context.ServerConfig
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            _context.Remove(dto);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteSpawnPointAsync(long id)
        {
            var dto = await _context.MapRegionAsset
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            _context.Remove(dto);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteUserAsync(long id)
        {
            var dto = await _context.UserConfig
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            _context.Remove(dto);
            await _context.SaveChangesAsync();
        }

        public async Task DuplicateMobAsync(long id)
        {
            var dto = await _context.MobConfig
                .AsNoTracking()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.Drops)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.BitsDrop)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto == null)
                return;

            // Mantém teu comportamento original
            var clonedEntity = (MobConfigDTO)dto.Clone();
            clonedEntity.Id = 0;

            // Corrige tracking de entidades filhas (impede leak de contextos anteriores)
            if (clonedEntity.Location != null)
                clonedEntity.Location.Id = 0;

            if (clonedEntity.ExpReward != null)
                clonedEntity.ExpReward.Id = 0;

            if (clonedEntity.DropReward != null)
            {
                clonedEntity.DropReward.Id = 0;

                if (clonedEntity.DropReward.Drops != null)
                {
                    foreach (var drop in clonedEntity.DropReward.Drops)
                        drop.Id = 0;
                }

                if (clonedEntity.DropReward.BitsDrop != null)
                    clonedEntity.DropReward.BitsDrop.Id = 0;
            }

            // ✅ Salva com segurança
            await _context.MobConfig.AddAsync(clonedEntity);
            await _context.SaveChangesAsync();
        }

        public async Task DuplicateSummonMobAsync(long id)
        {
            var dto = await _context.SummonsMobConfig
                .AsNoTracking()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward)
                .ThenInclude(y => y.Drops)
                .Include(x => x.DropReward)
                .ThenInclude(y => y.BitsDrop)
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
                {
                    // 🔹 Mantém tua estrutura original intacta — sem criar tipos novos
                    clonedEntity.DropReward = new SummonMobDropRewardDTO();
                }
                else
                {
                    clonedEntity.DropReward.Id = 0;

                    if (clonedEntity.DropReward.Drops != null)
                    {
                        foreach (var drop in clonedEntity.DropReward.Drops)
                            drop.Id = 0;
                    }

                    if (clonedEntity.DropReward.BitsDrop != null)
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

                // ✅ Só atualiza a senha se ela for preenchida
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
                // ❌ removido AsNoTracking() — causa tracking leak e conflitos
                .Include(x => x.Rewards)
                .SingleOrDefaultAsync(x => x.Id == scan.Id);

            if (dto == null)
            {
                _context.Add(scan);
            }
            else
            {
                // Remove os itens que não existem mais
                var parameterIds = scan.Rewards.Select(x => x.Id);
                var removeItems = dto.Rewards.Where(x => !parameterIds.Contains(x.Id)).ToList();
                foreach (var removeItem in removeItems)
                {
                    _context.Remove(removeItem);
                }

                // Adiciona os novos itens que não estão na base de dados
                var databaseIds = dto.Rewards.Select(x => x.Id);
                var newItems = scan.Rewards.Where(x => !databaseIds.Contains(x.Id)).ToList();
                foreach (var newItem in newItems)
                {
                    newItem.Id = 0;
                    newItem.ScanDetailAssetId = dto.Id;
                    _context.Add(newItem);
                }

                // Atualiza os dados básicos
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
                // ❌ Removido AsNoTracking() — causa leak ao tentar atualizar entidades relacionadas
                .Include(x => x.Rewards)
                .SingleOrDefaultAsync(x => x.Id == container.Id);

            if (dto == null)
            {
                _context.Add(container);
            }
            else
            {
                // Remove recompensas que não existem mais
                var parameterIds = container.Rewards.Select(x => x.Id);
                var removeItems = dto.Rewards.Where(x => !parameterIds.Contains(x.Id)).ToList();
                foreach (var removeItem in removeItems)
                {
                    _context.Remove(removeItem);
                }

                // Adiciona novas recompensas que não estão no banco
                var databaseIds = dto.Rewards.Select(x => x.Id);
                var newItems = container.Rewards.Where(x => !databaseIds.Contains(x.Id)).ToList();
                foreach (var newItem in newItems)
                {
                    newItem.Id = 0;
                    newItem.ContainerAssetId = dto.Id;
                    _context.Add(newItem);
                }

                // Atualiza os dados básicos do container
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
                // ❌ Removido AsNoTracking() — EF precisa rastrear para Update seguro
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
                // ❌ Removido AsNoTracking() — EF precisa rastrear a entidade para atualização limpa
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
                // ❌ Removido AsNoTracking()
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
                // ❌ Removido AsNoTracking() — EF precisa rastrear para Update correto
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
                // ❌ Removido AsNoTracking() — Remove precisa de entidade rastreada
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
                // ❌ Removido AsNoTracking() — EF precisa rastrear para Update/Add seguro
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
                // ❌ Removido AsNoTracking() — Remove precisa de tracking ativo
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
                    THEN '1' -- marcar como feita (poderia até usar outro valor se necessário)
                    ELSE '0' -- resetar como não feita
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

            // 🔹 1. Recuperar os QuestIds ativos
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

            // 🔸 2. Marcar como incompletas (reset)
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

            // 🔻 3. Deletar as quests
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
                // ❌ Removido AsNoTracking() — impede tracking e causa reattachment leaks
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
                // ❌ Removido AsNoTracking() — impede liberação adequada da entidade no pool
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
                // ❌ Removido AsNoTracking() — Update requer tracking ativo
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
                // ❌ Removido AsNoTracking() — Remove requer tracking ativo para evitar attach duplo
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
                // ❌ Removido AsNoTracking() — Update requer tracking ativo para liberação correta
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
                // ❌ Removido AsNoTracking() — Remove precisa de tracking ativo
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
                // ❌ Removido AsNoTracking() — Update precisa de tracking ativo
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
                // ❌ Removido AsNoTracking() — entidades incluídas precisam estar rastreadas
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
                // ❌ Removido AsNoTracking() — Remove precisa do tracking ativo
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
                // ✅ Aqui mantemos o AsNoTracking() porque o objetivo é CLONAR a entidade,
                // não alterá-la nem removê-la, e o clone é inserido como uma nova instância.
                .AsNoTracking()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.Drops)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.BitsDrop)
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
                    clonedEntity.DropReward.Drops.ToList().ForEach(drop => drop.Id = 0);
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
                            // ... inclua outros campos que precisa atualizar
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