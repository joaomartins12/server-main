using System.Reflection;
using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Mechanics;
using DigitalWorldOnline.Commons.Model.Character;
using DigitalWorldOnline.Commons.Models.Events;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Chat;
using DigitalWorldOnline.Commons.DTOs.Character;
using DigitalWorldOnline.Commons.DTOs.Digimon;
using DigitalWorldOnline.Commons.DTOs.Base;
using DigitalWorldOnline.Commons.DTOs.Chat;
using DigitalWorldOnline.Commons.Models.Asset;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace DigitalWorldOnline.Infrastructure.Repositories.Character
{
    public class CharacterCommandsRepository : ICharacterCommandsRepository
    {
        private readonly DatabaseContext _context;
        private readonly IMapper _mapper;

        public CharacterCommandsRepository(DatabaseContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<long> AddCharacterAsync(CharacterModel character)
        {
            var dto = _mapper.Map<CharacterDTO>(character);

            _context.Character.Add(dto);

            await _context.SaveChangesAsync();

            return dto.Id;
        }

        public async Task<DigimonDTO> AddDigimonAsync(DigimonModel digimon)
        {
            var tamerDto = await _context.Character
                .Include(x => x.Digimons)
                .ThenInclude(y => y.Digiclone)
                .ThenInclude(z => z.History)
                .Include(x => x.Digimons)
                .ThenInclude(y => y.AttributeExperience)
                .Include(x => x.Digimons)
                .ThenInclude(y => y.Location)
                .Include(x => x.Digimons)
                .ThenInclude(y => y.BuffList)
                .ThenInclude(z => z.Buffs)
                .Include(x => x.Digimons)
                .ThenInclude(y => y.Evolutions)
                .ThenInclude(z => z.Skills)
                .SingleOrDefaultAsync(x => x.Id == digimon.CharacterId);

            try
            {
                var dto = _mapper.Map<DigimonDTO>(digimon);

                if (tamerDto != null)
                {
                    var existing = tamerDto.Digimons.FirstOrDefault(d => d.Id == dto.Id);
                    if (existing != null)
                    {
                        _mapper.Map(digimon, existing); // atualiza ao invés de duplicar
                    }
                    else
                    {
                        tamerDto.Digimons.Add(dto); // adiciona se for novo
                    }

                    await _context.SaveChangesAsync();
                }

                return dto;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
        public async Task<CharacterFriendDTO> AddFriendAsync(CharacterFriendModel friend)
        {
            // 🔹 remove AsNoTracking
            var tamerDto = await _context.Character
                .Include(x => x.Friends)
                .SingleOrDefaultAsync(x => x.Id == friend.CharacterId);

            if (tamerDto == null)
                return null;

            var dto = _mapper.Map<CharacterFriendDTO>(friend);

            // 🔹 apenas adiciona à coleção — o EF já está a rastrear
            tamerDto.Friends.Add(dto);

            await _context.SaveChangesAsync(); // sem necessidade de _context.Update
            return dto;
        }

        public async Task<DeleteCharacterResultEnum> DeleteCharacterByAccountAndPositionAsync(long accountId, byte characterPosition)
        {
            try
            {
                var dto = await _context.Character
                    .AsNoTracking() // ✅ permitido aqui
                    .AsSplitQuery()
                    .Include(x => x.Incubator)
                    .Include(x => x.Location)
                    .Include(x => x.Xai)
                    .Include(x => x.TimeReward)
                    .Include(x => x.AttendanceReward)
                    .Include(x => x.ActiveSkill)
                    .Include(x => x.DailyPoints)
                    .Include(x => x.ConsignedShop)
                    .Include(x => x.MapRegions)
                    .Include(x => x.Points)
                    .Include(x => x.BuffList)
                        .ThenInclude(y => y.Buffs)
                    .Include(x => x.SealList)
                        .ThenInclude(y => y.Seals)
                    .Include(x => x.ItemList)
                        .ThenInclude(y => y.Items)
                    .Include(x => x.Digimons)
                        .ThenInclude(y => y.Digiclone)
                    .Include(x => x.Digimons)
                        .ThenInclude(y => y.AttributeExperience)
                    .Include(x => x.Digimons)
                        .ThenInclude(y => y.Location)
                    .Include(x => x.Digimons)
                        .ThenInclude(y => y.BuffList)
                        .ThenInclude(z => z.Buffs)
                    .Include(x => x.Digimons)
                        .ThenInclude(z => z.Evolutions)
                    .SingleOrDefaultAsync(x => x.AccountId == accountId &&
                                               x.Position == characterPosition);

                if (dto != null)
                {
                    _context.Remove(dto);
                    await _context.SaveChangesAsync();
                }

                return DeleteCharacterResultEnum.Deleted;
            }
            catch
            {
                return DeleteCharacterResultEnum.Error;
            }
        }

        public async Task UpdateCharacterChannelByIdAsync(long characterId, byte channel)
        {
            var character = await _context.Character.FirstOrDefaultAsync(x => x.Id == characterId);

            if (character == null) return;

            character.Channel = channel;

            _context.Character.Update(character);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterLocationAsync(CharacterLocationModel location)
        {
            var dto = await _context.CharacterLocation.FindAsync(location.Id);

            if (dto == null) return;

            dto.MapId = location.MapId;
            dto.X = location.X;
            dto.Y = location.Y;
            dto.Z = location.Z;

            _context.CharacterLocation.Update(dto);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateDigimonLocationAsync(DigimonLocationModel location)
        {
            var dto = await _context.DigimonLocation.FindAsync(location.Id);

            if (dto == null) return;

            dto.MapId = location.MapId;
            dto.X = location.X;
            dto.Y = location.Y;
            dto.Z = location.Z;

            _context.DigimonLocation.Update(dto);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterResourcesAsync(CharacterModel tamer)
        {
            // ❌ Remover AsNoTracking
            var tamerDto = await _context.Character
                .Include(x => x.Digimons)
                .FirstOrDefaultAsync(x => x.Id == tamer.Id);

            if (tamerDto == null)
                return;

            // 🔹 Atualiza apenas os campos necessários
            tamerDto.CurrentHp = tamer.CurrentHp;
            tamerDto.CurrentDs = tamer.CurrentDs;

            // 🔹 Atualiza HP/DS dos Digimons se houver
            if (tamer.Digimons?.Any() == true)
            {
                foreach (var digimon in tamerDto.Digimons)
                {
                    var source = tamer.Digimons.FirstOrDefault(d => d.Id == digimon.Id);
                    if (source != null)
                    {
                        digimon.CurrentHp = source.CurrentHp;
                        digimon.CurrentDs = source.CurrentDs;
                    }
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharactersStateAsync(CharacterStateEnum state)
        {
            var characters = await _context.Character.ToListAsync(); // ✅ sem AsNoTracking()

            foreach (var character in characters)
            {
                character.State = state;
                character.EventState = CharacterEventStateEnum.None;
            }

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterStateByIdAsync(long characterId, CharacterStateEnum state)
        {
            var character = await _context.Character.FirstOrDefaultAsync(x => x.Id == characterId); // ✅ sem AsNoTracking

            if (character == null)
                return;

            character.State = state;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterExperienceAsync(long tamerId, long currentExperience, byte level)
        {
            var dto = await _context.Character.FindAsync(tamerId);

            if (dto == null) return;

            dto.CurrentExperience = currentExperience;
            dto.Level = level;

            _context.Character.Update(dto);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateDigimonExperienceAsync(DigimonModel digimon)
        {
            // ❌ Remover AsNoTracking
            var dto = await _context.Digimon
                .Include(x => x.Evolutions)
                .Include(x => x.AttributeExperience)
                .FirstOrDefaultAsync(x => x.Id == digimon.Id);

            if (dto == null)
                return;

            // 🔹 Atualiza os valores principais
            dto.CurrentExperience = digimon.CurrentExperience;
            dto.CurrentSkillExperience = digimon.CurrentSkillExperience;
            dto.TranscendenceExperience = digimon.TranscendenceExperience;
            dto.Level = digimon.Level;

            // 🔹 Atualiza AttributeExperience
            var attrSrc = digimon.AttributeExperience;
            var attrDst = dto.AttributeExperience;

            if (attrSrc != null && attrDst != null)
            {
                attrDst.Data = attrSrc.Data;
                attrDst.Vaccine = attrSrc.Vaccine;
                attrDst.Virus = attrSrc.Virus;
                attrDst.Ice = attrSrc.Ice;
                attrDst.Water = attrSrc.Water;
                attrDst.Fire = attrSrc.Fire;
                attrDst.Land = attrSrc.Land;
                attrDst.Wind = attrSrc.Wind;
                attrDst.Wood = attrSrc.Wood;
                attrDst.Light = attrSrc.Light;
                attrDst.Dark = attrSrc.Dark;
                attrDst.Thunder = attrSrc.Thunder;
                attrDst.Steel = attrSrc.Steel;
            }

            // 🔹 Atualiza Evoluções
            if (digimon.Evolutions?.Any() == true)
            {
                foreach (var evo in dto.Evolutions)
                {
                    var srcEvo = digimon.Evolutions.FirstOrDefault(x => x.Id == evo.Id);
                    if (srcEvo == null)
                        continue;

                    evo.Type = srcEvo.Type;
                    evo.Unlocked = srcEvo.Unlocked;
                    evo.SkillPoints = srcEvo.SkillPoints;
                    evo.SkillMastery = srcEvo.SkillMastery;
                    evo.SkillExperience = srcEvo.SkillExperience;
                }
            }

            // ✅ Sem Update(dto)
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterSealsAsync(CharacterSealListModel sealList)
        {
            var dto = await _context.CharacterSealList
                .AsNoTracking()
                .Include(x => x.Seals)
                .FirstOrDefaultAsync(x => x.Id == sealList.Id);

            if (dto != null)
            {
                dto.SealLeaderId = sealList.SealLeaderId;

                foreach (var seal in sealList.Seals)
                {
                    var dtoSeal = dto.Seals.FirstOrDefault(x => x.Id == seal.Id);
                    if (dtoSeal != null)
                    {
                        dtoSeal.SealId = seal.SealId;
                        dtoSeal.SequentialId = seal.SequentialId;
                        dtoSeal.Favorite = seal.Favorite;
                        dtoSeal.Amount = seal.Amount;
                        _context.Update(dtoSeal);
                    }
                    else
                    {
                        dtoSeal = _mapper.Map<CharacterSealDTO>(seal);
                        dtoSeal.SealListId = sealList.Id;
                        dto.Seals.Add(dtoSeal);
                        _context.Add(dtoSeal);
                    }
                }

                _context.Update(dto);
                _context.SaveChanges();
            }
        }

        public async Task AddChatMessageAsync(ChatMessageModel chatMessage)
        {
            var dto = _mapper.Map<ChatMessageDTO>(chatMessage);
            if (dto != null)
            {
                _context.Add(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdatePartnerCurrentTypeAsync(DigimonModel digimon)
        {
            var dto = await _context.Digimon.FirstOrDefaultAsync(x => x.Id == digimon.Id);

            if (dto == null)
                return;

            dto.CurrentType = digimon.CurrentType;

            // O EF já rastreia a entidade, então só salvar
            await _context.SaveChangesAsync();
        }

        public async Task UpdateDigicloneAsync(DigimonDigicloneModel digiclone)
        {
            var dto = await _context.DigimonDigiclone
                .Include(x => x.History)
                .FirstOrDefaultAsync(x => x.Id == digiclone.Id || x.DigimonId == digiclone.DigimonId);

            if (dto == null)
                return;

            dto.ATLevel = digiclone.ATLevel;
            dto.BLLevel = digiclone.BLLevel;
            dto.CTLevel = digiclone.CTLevel;
            dto.EVLevel = digiclone.EVLevel;
            dto.HPLevel = digiclone.HPLevel;

            dto.ATValue = digiclone.ATValue;
            dto.BLValue = digiclone.BLValue;
            dto.CTValue = digiclone.CTValue;
            dto.EVValue = digiclone.EVValue;
            dto.HPValue = digiclone.HPValue;

            if (dto.History != null && digiclone.History != null)
            {
                dto.History.ATValues = digiclone.History.ATValues;
                dto.History.BLValues = digiclone.History.BLValues;
                dto.History.CTValues = digiclone.History.CTValues;
                dto.History.EVValues = digiclone.History.EVValues;
                dto.History.HPValues = digiclone.History.HPValues;
            }

            // O EF já está a rastrear tudo
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterTitleByIdAsync(long characterId, short titleId)
        {
            var dto = await _context.Character.FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto == null)
                return;

            dto.CurrentTitle = titleId;

            // Não precisa de _context.Update(dto), o EF já está a rastrear
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterProgressCompleteAsync(CharacterProgressModel progress)
        {
            var dto = await _context.CharacterProgress.FirstOrDefaultAsync(x => x.Id == progress.Id);

            if (dto == null)
                return;

            dto.CompletedData = progress.CompletedData;
            dto.CompletedDataValue = progress.CompletedDataValue;

            // EF rastreia a entidade automaticamente
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterBuffListAsync(CharacterBuffListModel buffList)
        {
            var dto = await _context.CharacterBuffList
                .Include(x => x.Buffs)
                .FirstOrDefaultAsync(x => x.Id == buffList.Id);

            if (dto == null)
                return;

            var buffsToRemove = dto.Buffs
                .Where(dtoBuff => !buffList.Buffs.Any(buff => buff.Id == dtoBuff.Id))
                .ToList();

            foreach (var buffToRemove in buffsToRemove)
            {
                _context.Remove(buffToRemove);
            }

            foreach (var buff in buffList.Buffs.Where(x => !x.Expired))
            {
                var existingBuff = dto.Buffs.FirstOrDefault(x => x.Id == buff.Id);
                if (existingBuff != null)
                {
                    existingBuff.Duration = buff.Duration;
                    existingBuff.EndDate = buff.EndDate;
                    existingBuff.SkillId = buff.SkillId;
                    existingBuff.TypeN = buff.TypeN;
                    _context.Update(existingBuff); // Opcional, pois é rastreado
                }
                else
                {
                    var newBuff = _mapper.Map<CharacterBuffDTO>(buff);
                    newBuff.BuffListId = buffList.Id;
                    dto.Buffs.Add(newBuff);
                    _context.Add(newBuff);
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // 🔁 Silenciar ou logar conforme necessidade
                Console.WriteLine($"[WARN] Concurrency conflict on buffs update: {ex.Message}");
                // Você pode ignorar, logar, ou tomar outras ações como re-tentar.
            }
        }

        public async Task UpdateDigimonBuffListAsync(DigimonBuffListModel buffList)
        {
            // ❌ Removido AsNoTracking — causava erros de concorrência e tracking incorreto
            var dto = await _context.DigimonBuffList
                .Include(x => x.Buffs)
                .FirstOrDefaultAsync(x => x.Id == buffList.Id);

            if (dto == null)
                return;

            // 🔹 Remove buffs que não existem mais
            var buffsToRemove = dto.Buffs
                .Where(dtoBuff => !buffList.Buffs.Any(buff => buff.Id == dtoBuff.Id))
                .ToList();

            if (buffsToRemove.Count > 0)
            {
                _context.RemoveRange(buffsToRemove);
            }

            // 🔹 Atualiza ou adiciona novos buffs
            foreach (var buff in buffList.Buffs)
            {
                var dtoBuff = dto.Buffs.FirstOrDefault(b => b.Id == buff.Id);

                if (dtoBuff != null)
                {
                    dtoBuff.Duration = buff.Duration;
                    dtoBuff.EndDate = buff.EndDate;
                    dtoBuff.SkillId = buff.SkillId;
                    dtoBuff.TypeN = buff.TypeN;
                    dtoBuff.CoolEndDate = buff.CoolEndDate;
                    dtoBuff.Cooldown = buff.Cooldown;
                }
                else
                {
                    var newBuff = _mapper.Map<DigimonBuffDTO>(buff);
                    newBuff.BuffListId = buffList.Id;
                    dto.Buffs.Add(newBuff);
                    _context.Add(newBuff);
                }
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // ⚠️ Ocorre se outro processo alterou os buffs simultaneamente
                Console.WriteLine($"[WARN] Concurrency conflict on DigimonBuffList update: {ex.Message}");
            }
        }

        public async Task UpdateCharacterActiveEvolutionAsync(CharacterActiveEvolutionModel activeEvolution)
        {
            var dto = await _context.CharacterActiveEvolution.FindAsync(activeEvolution.Id);

            if (dto == null) return;

            dto.XgPerSecond = activeEvolution.XgPerSecond;
            dto.DsPerSecond = activeEvolution.DsPerSecond;

            _context.CharacterActiveEvolution.Update(dto);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterBasicInfoAsync(CharacterModel character)
        {
            var dto = await _context.Character
                .Include(x => x.Digimons)
                .SingleOrDefaultAsync(x => x.Id == character.Id);

            if (dto == null)
                return;

            // 🔹 Atualiza apenas os campos necessários
            dto.CurrentHp = character.CurrentHp;
            dto.CurrentDs = character.CurrentDs;
            dto.XGauge = character.XGauge;
            dto.XCrystals = character.XCrystals;

            // 🔹 Atualiza os Digimons relacionados
            foreach (var digimonDto in dto.Digimons)
            {
                var digimonModel = character.Digimons.FirstOrDefault(x => x.Id == digimonDto.Id);
                if (digimonModel != null)
                {
                    digimonDto.CurrentHp = digimonModel.CurrentHp;
                    digimonDto.CurrentDs = digimonModel.CurrentDs;
                    digimonDto.CurrentType = digimonModel.CurrentType;
                }
            }

            // 🔹 Apenas salva as alterações rastreadas (sem reattach)
            await _context.SaveChangesAsync();
        }

        public async Task UpdateItemListBitsAsync(long itemListId, long bits)
        {
            var dto = await _context.ItemLists.FirstOrDefaultAsync(x => x.Id == itemListId);

            if (dto == null)
                return;

            // Atualiza apenas o campo necessário
            dto.Bits = bits;

            // O EF Core já rastreia a entidade, então basta salvar
            await _context.SaveChangesAsync();
        }

        public async Task UpdateItemsAsync(List<ItemModel> items)
        {
            await RemoveDeletedItems(items);
            await AddOrUpdateItems(items);
            await _context.SaveChangesAsync();
        }

        private async Task AddOrUpdateItems(List<ItemModel> items)
        {
            if (!items.Any()) return;

            foreach (var item in items.ToList())
            {
                var dto = await _context.Items
                    .Include(x => x.AccessoryStatus)
                    .Include(x => x.SocketStatus)
                    .FirstOrDefaultAsync(x => x.Id == item.Id);

                if (dto != null)
                {
                    dto.Slot = item.Slot;
                    dto.Amount = item.Amount;
                    dto.ItemId = item.ItemId;
                    dto.Duration = item.Duration;
                    dto.EndDate = item.EndDate;
                    dto.FirstExpired = item.FirstExpired;
                    if (item.ItemListId > 0) dto.ItemListId = item.ItemListId;
                    dto.RerollLeft = item.RerollLeft;
                    dto.FamilyType = item.FamilyType;
                    dto.Power = item.Power;
                    dto.TamerShopSellPrice = item.TamerShopSellPrice;

                    foreach (var dtoStatus in dto.AccessoryStatus)
                    {
                        var modelStatus = item.AccessoryStatus.First(x => x.Slot == dtoStatus.Slot);
                        dtoStatus.Type = modelStatus.Type;
                        dtoStatus.Value = modelStatus.Value;
                    }

                    foreach (var dtoStatus in dto.SocketStatus)
                    {
                        var modelStatus = item.SocketStatus.First(x => x.Slot == dtoStatus.Slot);
                        dtoStatus.Type = modelStatus.Type;
                        dtoStatus.AttributeId = modelStatus.AttributeId;
                        dtoStatus.Value = modelStatus.Value;
                    }
                }
                else
                {
                    await _context.AddAsync(_mapper.Map<ItemDTO>(item));
                }
            }
        }


        private async Task RemoveDeletedItems(List<ItemModel> items)
        {
            if (!items.Any())
                return;

            var itemListId = items.First().ItemListId;
            var existingIds = items.Select(x => x.Id).ToList();

            // Remove diretamente no SQL (sem carregar entidades)
            await _context.Items
                .Where(x => x.ItemListId == itemListId && !existingIds.Contains(x.Id))
                .ExecuteDeleteAsync();
        }

        public async Task UpdateItemAccessoryStatusAsync(ItemModel item)
        {
            var dto = await _context.Items
                .Include(x => x.AccessoryStatus)
                .FirstOrDefaultAsync(x => x.Id == item.Id);

            if (dto == null)
                return;

            dto.RerollLeft = item.RerollLeft;
            dto.Power = item.Power;

            foreach (var dtoStatus in dto.AccessoryStatus)
            {
                var modelStatus = item.AccessoryStatus.FirstOrDefault(x => x.Slot == dtoStatus.Slot);
                if (modelStatus == null) continue;

                dtoStatus.Type = modelStatus.Type;
                dtoStatus.Value = modelStatus.Value;
            }

            await _context.SaveChangesAsync();
        }

        public async Task UpdateItemSocketStatusAsync(ItemModel item)
        {
            var dto = await _context.Items
                .Include(x => x.SocketStatus)
                .FirstOrDefaultAsync(x => x.Id == item.Id);

            if (dto == null)
                return;

            dto.RerollLeft = item.RerollLeft;
            dto.Power = item.Power;

            foreach (var dtoStatus in dto.SocketStatus)
            {
                var modelStatus = item.SocketStatus.FirstOrDefault(x => x.Slot == dtoStatus.Slot);
                if (modelStatus == null) continue;

                dtoStatus.Type = modelStatus.Type;
                dtoStatus.AttributeId = modelStatus.AttributeId;
                dtoStatus.Value = modelStatus.Value;
            }

            await _context.SaveChangesAsync();
        }

        public async Task UpdateItemAsync(ItemModel item)
        {
            var dto = await _context.Items
                .Include(x => x.AccessoryStatus)
                .Include(x => x.SocketStatus)
                .FirstOrDefaultAsync(x => x.Id == item.Id);

            if (dto != null)
            {
                dto.Amount = item.Amount;
                dto.ItemId = item.ItemId;
                dto.Duration = item.Duration;
                dto.EndDate = item.EndDate;
                dto.FirstExpired = item.FirstExpired;
                if (item.ItemListId > 0) dto.ItemListId = item.ItemListId;
                dto.RerollLeft = item.RerollLeft;
                dto.Power = item.Power;
                dto.TamerShopSellPrice = item.TamerShopSellPrice;

                foreach (var dtoStatus in dto.AccessoryStatus)
                {
                    var modelStatus = item.AccessoryStatus.First(x => x.Slot == dtoStatus.Slot);
                    dtoStatus.Type = modelStatus.Type;
                    dtoStatus.Value = modelStatus.Value;
                }

                foreach (var dtoStatus in dto.SocketStatus)
                {
                    var modelStatus = item.SocketStatus.First(x => x.Slot == dtoStatus.Slot);
                    dtoStatus.Type = modelStatus.Type;
                    dtoStatus.AttributeId = modelStatus.AttributeId;
                    dtoStatus.Value = modelStatus.Value;
                }

                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateItemListSizeAsync(long itemListId, byte newSize)
        {
            var dto = await _context.ItemLists.FirstOrDefaultAsync(x => x.Id == itemListId);

            if (dto != null)
            {
                dto.Size = newSize;

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task AddInventorySlotsAsync(List<ItemModel> items)
        {
            if (items == null || items.Count == 0)
                return;

            var itemListId = items.First().ItemListId;

            // 🔹 Busca rastreada (sem AsNoTracking)
            var itemListDto = await _context.ItemLists
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == itemListId);

            if (itemListDto == null)
                return;

            // 🔹 Cria todos os itens de uma vez só
            var newItems = items.Select(item => _mapper.Map<ItemDTO>(item)).ToList();
            await _context.Items.AddRangeAsync(newItems);

            // 🔹 Atualiza o tamanho apenas uma vez
            itemListDto.Size += (byte)newItems.Count;

            // 🔹 Apenas salva alterações rastreadas (sem reattachs)
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterEventStateByIdAsync(long characterId, CharacterEventStateEnum state)
        {
            var dto = await _context.Character.FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto != null)
            {
                dto.EventState = state;

                _context.Update(dto);

                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateEvolutionAsync(DigimonEvolutionModel evolution)
        {
            var dto = await _context.DigimonEvolution
                .FirstOrDefaultAsync(x => x.Id == evolution.Id);

            if (dto == null)
                return;

            dto.Type = evolution.Type;
            dto.Unlocked = evolution.Unlocked;
            dto.SkillPoints = evolution.SkillPoints;
            dto.SkillMastery = evolution.SkillMastery;
            dto.SkillExperience = evolution.SkillExperience;

            dto.Skills = _mapper.Map<List<DigimonEvolutionSkillDTO>>(evolution.Skills);

            await _context.SaveChangesAsync();
        }

        public async Task UpdateIncubatorAsync(CharacterIncubatorModel incubator)
        {
            var dto = await _context.CharacterIncubator
                .FirstOrDefaultAsync(x => x.Id == incubator.Id);

            if (dto == null)
                return;

            dto.EggId = incubator.EggId;
            dto.HatchLevel = incubator.HatchLevel;
            dto.BackupDiskId = incubator.BackupDiskId;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterMapRegionAsync(CharacterMapRegionModel mapRegion)
        {
            var dto = await _context.CharacterMapRegion
                .FirstOrDefaultAsync(x => x.Id == mapRegion.Id);

            if (dto == null)
                return;

            dto.Unlocked = mapRegion.Unlocked;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateDigimonSizeAsync(long digimonId, short size)
        {
            var dto = await _context.Digimon.FindAsync(digimonId);

            if (dto == null) return;

            dto.Size = size;

            _context.Digimon.Update(dto);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterInitialPacketSentOnceSentAsync(long characterId, bool sendOnceSent)
        {
            var dto = await _context.Character.FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto == null) return;

            dto.InitialPacketSentOnceSent = sendOnceSent;

            _context.Character.Update(dto);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterSizeAsync(long characterId, short size)
        {
            var dto = await _context.Character
                .FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto == null)
                return;

            dto.Size = size;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateDigimonGradeAsync(long digimonId, DigimonHatchGradeEnum grade)
        {
            var dto = await _context.Digimon
                .FirstOrDefaultAsync(x => x.Id == digimonId);

            if (dto == null)
                return;

            dto.HatchGrade = grade;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterDigimonsOrderAsync(CharacterModel character)
        {
            foreach (var digimon in character.Digimons)
            {
                var dto = await _context.Digimon
                    .FirstOrDefaultAsync(x => x.Id == digimon.Id);

                if (dto != null)
                {
                    dto.Slot = digimon.Slot;
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task DeleteDigimonAsync(long digimonId)
        {
            var dto = await _context.Digimon
                .FirstOrDefaultAsync(x => x.Id == digimonId);

            if (dto == null)
                return;

            _context.Digimon.Remove(dto);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterDigimonArchiveItemAsync(
            CharacterDigimonArchiveItemModel characterDigimonArchiveItem)
        {
            var dto = await _context.CharacterDigimonArchiveItem
                .FirstOrDefaultAsync(x => x.Id == characterDigimonArchiveItem.Id);

            if (dto == null)
                return;

            dto.DigimonId = characterDigimonArchiveItem.DigimonId;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateDigimonSlotAsync(long digimonId, byte digimonSlot)
        {
            var dto = await _context.Digimon
                .FirstOrDefaultAsync(x => x.Id == digimonId);

            if (dto == null)
                return;

            dto.Slot = digimonSlot;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterXaiAsync(CharacterXaiModel xai)
        {
            var dto = await _context.CharacterXai
                .FirstOrDefaultAsync(x => x.Id == xai.Id);

            if (dto == null)
                return;

            dto.ItemId = xai.ItemId;
            dto.XCrystals = xai.XCrystals;
            dto.XGauge = xai.XGauge;

            await _context.SaveChangesAsync();
        }

        public async Task AddDigimonArchiveSlotAsync(Guid archiveId, CharacterDigimonArchiveItemModel archiveItem)
        {
            var archiveDto = await _context.CharacterDigimonArchive
                .Include(x => x.DigimonArchives)
                .FirstOrDefaultAsync(x => x.Id == archiveId);

            if (archiveDto == null)
                return;

            var dto = _mapper.Map<CharacterDigimonArchiveItemDTO>(archiveItem);
            dto.DigimonArchiveId = archiveId;

            _context.CharacterDigimonArchiveItem.Add(dto);
            archiveDto.Slots++;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterDigimonSlotsAsync(long characterId, byte slots)
        {
            var characterDto = await _context.Character
                .FirstOrDefaultAsync(x => x.Id == characterId);

            if (characterDto == null)
                return;

            characterDto.DigimonSlots = slots;

            await _context.SaveChangesAsync();
        }

        public async Task<CharacterDTO> ChangeCharacterNameAsync(long characterId, string newCharacterName)
        {
            var dto = await _context.Character.FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto == null)
                return null;

            dto.Name = newCharacterName;

            await _context.SaveChangesAsync();
            return dto;
        }

        public async Task<CharacterDTO> ChangeCharacterIdTpAsync(long characterId, int targetTamerIdTP)
        {
            var dto = await _context.Character.FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto == null)
                return null;

            dto.TargetTamerIdTP = targetTamerIdTP;

            await _context.SaveChangesAsync();
            return dto;
        }

        public async Task<DigimonDTO> ChangeDigimonNameAsync(long digimonId, string NewDigimonName)
        {
            var dto = await _context.Digimon.FirstOrDefaultAsync(x => x.Id == digimonId);
            if (dto == null)
                return null;

            dto.Name = NewDigimonName;
            await _context.SaveChangesAsync();
            return dto;
        }

        public async Task<CharacterDTO> ChangeTamerModelAsync(long characterId, CharacterModelEnum model)
        {
            var dto = await _context.Character.FirstOrDefaultAsync(x => x.Id == characterId);

            if (dto == null)
                return null;

            dto.Model = model;

            await _context.SaveChangesAsync();
            return dto;
        }

        public async Task UpdateTamerSkillCooldownAsync(CharacterTamerSkillModel activeSkill)
        {
            var dto = await _context.ActiveSkills.FirstOrDefaultAsync(x => x.Id == activeSkill.Id);

            if (dto == null)
                return;

            dto.SkillId = activeSkill.SkillId;
            dto.Cooldown = activeSkill.Cooldown;
            dto.EndCooldown = activeSkill.EndCooldown;
            dto.Type = activeSkill.Type;
            dto.Duration = activeSkill.Duration;
            dto.EndDate = activeSkill.EndDate;

            await _context.SaveChangesAsync();
        }

        public async Task AddInventorySlotAsync(ItemModel newSlot)
        {
            var itemListDto = await _context.ItemLists
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == newSlot.ItemListId);

            if (itemListDto == null)
                return;

            var dto = _mapper.Map<ItemDTO>(newSlot);
            await _context.Items.AddAsync(dto);

            itemListDto.Size += 1;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterArenaPointsAsync(CharacterArenaPointsModel points)
        {
            var dto = await _context.CharacterPoints
                .FirstOrDefaultAsync(x => x.Id == points.Id);

            if (dto == null)
                return;

            dto.CurrentStage = points.CurrentStage;
            dto.Amount = points.Amount;
            dto.ItemId = points.ItemId;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterInProgressAsync(InProgressQuestModel progress)
        {
            var dto = await _context.InProgressQuest
                .FirstOrDefaultAsync(x => x.Id == progress.Id);

            if (dto == null)
                return;

            dto.FirstCondition = progress.FirstCondition;
            dto.SecondCondition = progress.SecondCondition;
            dto.ThirdCondition = progress.ThirdCondition;
            dto.FourthCondition = progress.FourthCondition;
            dto.FifthCondition = progress.FifthCondition;

            await _context.SaveChangesAsync();
        }

        public async Task AddCharacterProgressAsync(CharacterProgressModel progress)
        {
            var dto = await _context.CharacterProgress
                .Include(x => x.InProgressQuestData)
                .FirstOrDefaultAsync(x => x.Id == progress.Id);

            if (dto != null)
            {
                var questsToAdd = progress.InProgressQuestData
                    .Where(quest => dto.InProgressQuestData.All(q => q.Id != quest.Id))
                    .ToList();

                foreach (var newQuest in questsToAdd)
                {
                    var questDto = _mapper.Map<InProgressQuestDTO>(newQuest);
                    questDto.CharacterProgressId = progress.Id;

                    _context.InProgressQuest.Add(questDto);
                    await _context.SaveChangesAsync();
                }
            }
        }

        public async Task UpdateTamerAttendanceRewardAsync(AttendanceRewardModel attendanceRewardModel)
        {
            var dto = await _context.AttendanceReward
                .FirstOrDefaultAsync(x => x.CharacterId == attendanceRewardModel.CharacterId);

            if (dto != null)
            {
                if (dto.LastRewardDate.Month != DateTime.Now.Month)
                {
                    dto.TotalDays = 0;
                }

                dto.LastRewardDate = attendanceRewardModel.LastRewardDate;
                dto.TotalDays = attendanceRewardModel.TotalDays;

                _context.AttendanceReward.Update(dto);
                await _context.SaveChangesAsync();
            }
        }
        public async Task UpdateCharacterDeckBuffAsync(CharacterModel character)
        {
            var dto = await _context.Character
                .Include(x => x.DeckBuff)
                    .ThenInclude(x => x.Options)
                        .ThenInclude(x => x.DeckBookInfo)
                .FirstOrDefaultAsync(x => x.Id == character.Id);

            if (dto != null)
            {
                // Atualiza apenas o DeckBuffId
                dto.DeckBuffId = character.DeckBuffId;

                // Não atualiza o objeto DeckBuff diretamente, apenas o Id.
                // Isso evita sobrescrever ou remover o DeckBuff da enciclopédia.

                _context.Update(dto);
                await _context.SaveChangesAsync();
            }
        }

        public async Task UpdateTamerTimeRewardAsync(TimeRewardModel timeRewardModel)
        {
            var dto = await _context.TimeReward
                .FirstOrDefaultAsync(x => x.CharacterId == timeRewardModel.CharacterId);

            if (dto == null)
                return;

            dto.StartTime = timeRewardModel.StartTime;
            dto.RewardIndex = timeRewardModel.RewardIndex;
            dto.AtualTime = timeRewardModel.AtualTime;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterArenaDailyPointsAsync(CharacterArenaDailyPointsModel points)
        {
            var dto = await _context.CharacterDailyPoints
                .FirstOrDefaultAsync(x => x.Id == points.Id);

            if (dto == null)
                return;

            dto.InsertDate = points.InsertDate;
            dto.Points = points.Points;

            await _context.SaveChangesAsync();
        }

        public async Task<CharacterEncyclopediaModel> CreateCharacterEncyclopediaAsync(
    CharacterEncyclopediaModel characterEncyclopedia)
        {
            var tamerDto = await _context.Character
                .Include(x => x.Encyclopedia)
                .ThenInclude(x => x.Evolutions)
                .SingleOrDefaultAsync(x => x.Id == characterEncyclopedia.CharacterId);

            if (tamerDto == null)
                return null;

            var dto = _mapper.Map<CharacterEncyclopediaDTO>(characterEncyclopedia);

            try
            {
                // adiciona ao conjunto rastreado diretamente
                tamerDto.Encyclopedia.Add(dto);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            return _mapper.Map<CharacterEncyclopediaModel>(dto);
        }

        public async Task UpdateCharacterEncyclopediaAsync(CharacterEncyclopediaModel characterEncyclopedia)
        {
            var dto = await _context.CharacterEncyclopedia
                .Include(x => x.Evolutions)
                .SingleOrDefaultAsync(x => x.Id == characterEncyclopedia.Id);

            if (dto == null)
                return;

            try
            {
                // Atualiza propriedades principais
                dto.Level = characterEncyclopedia.Level;
                dto.Size = characterEncyclopedia.Size;
                dto.EnchantAT = characterEncyclopedia.EnchantAT;
                dto.EnchantBL = characterEncyclopedia.EnchantBL;
                dto.EnchantCT = characterEncyclopedia.EnchantCT;
                dto.EnchantEV = characterEncyclopedia.EnchantEV;
                dto.EnchantHP = characterEncyclopedia.EnchantHP;
                dto.IsRewardAllowed = characterEncyclopedia.IsRewardAllowed;
                dto.IsRewardReceived = characterEncyclopedia.IsRewardReceived;
                dto.CreateDate = DateTime.Now;

                // Atualiza ou adiciona evoluções
                foreach (var evolutionModel in characterEncyclopedia.Evolutions)
                {
                    var existing = dto.Evolutions.FirstOrDefault(x => x.Id == evolutionModel.Id);

                    if (existing != null)
                    {
                        existing.IsUnlocked = evolutionModel.IsUnlocked;
                        existing.CreateDate = DateTime.Now;
                    }
                    else
                    {
                        var newEvolution = _mapper.Map<CharacterEncyclopediaEvolutionsDTO>(evolutionModel);
                        newEvolution.CharacterEncyclopediaId = dto.Id;
                        dto.Evolutions.Add(newEvolution);
                    }
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Encyclopedia Update Error] {e.Message}\n{e.StackTrace}");
                throw;
            }
        }

        public async Task UpdateCharacterEncyclopediaEvolutionsAsync(
    CharacterEncyclopediaEvolutionsModel characterEncyclopediaEvolution)
        {
            var dto = await _context.CharacterEncyclopediaEvolutions
                .FirstOrDefaultAsync(x => x.Id == characterEncyclopediaEvolution.Id);

            if (dto == null)
                return;

            dto.IsUnlocked = characterEncyclopediaEvolution.IsUnlocked;
            dto.CreateDate = DateTime.Now;

            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharacterFriendsAsync(CharacterModel? character, bool connected = false)
        {
            // Fetch and update records
            List<CharacterFriendDTO> dto;
            if (character != null)
            {
                dto = await _context.CharacterFriends
                    .Where(x => x.FriendId == character.Id)
                    .ToListAsync();
            }
            else
            {
                dto = await _context.CharacterFriends
                    .ToListAsync();
            }

            if (!dto.IsNullOrEmpty())
            {
                dto.ForEach(friend => friend.SetConnected(connected));
                await _context.SaveChangesAsync();
            }
        }
    }
}