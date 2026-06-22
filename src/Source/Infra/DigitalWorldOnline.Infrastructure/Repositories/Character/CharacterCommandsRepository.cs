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
                .Include(x => x.Digimons).ThenInclude(y => y.Digiclone).ThenInclude(z => z.History)
                .Include(x => x.Digimons).ThenInclude(y => y.AttributeExperience)
                .Include(x => x.Digimons).ThenInclude(y => y.Location)
                .Include(x => x.Digimons).ThenInclude(y => y.BuffList).ThenInclude(z => z.Buffs)
                .Include(x => x.Digimons).ThenInclude(y => y.Evolutions).ThenInclude(z => z.Skills)
                .SingleOrDefaultAsync(x => x.Id == digimon.CharacterId);

            try
            {
                var dto = _mapper.Map<DigimonDTO>(digimon);

                if (tamerDto != null)
                {
                    var existing = tamerDto.Digimons.FirstOrDefault(d => d.Id == dto.Id);
                    if (existing != null)
                    {
                        // Atualiza campos do existente (sem recriar a coleção)
                        _mapper.Map(digimon, existing);
                        _context.Digimon.Update(existing);
                    }
                    else
                    {
                        // 🔹 Garante vínculo mesmo que CharacterId do digimon venha nulo
                        dto.CharacterId = digimon.CharacterId ?? tamerDto.Id;

                        await _context.Digimon.AddAsync(dto);

                        // (Opcional) mantém a navegação também
                        tamerDto.Digimons.Add(dto);
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
            var tamerDto = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .Include(x => x.Friends)
                .SingleOrDefaultAsync(x => x.Id == friend.CharacterId)
                .ConfigureAwait(false);

            if (tamerDto == null) return null;

            var dto = _mapper.Map<CharacterFriendDTO>(friend);
            tamerDto.Friends.Add(dto);

            _context.Update(tamerDto);
            await _context.SaveChangesAsync().ConfigureAwait(false);

            return dto;
        }

        public async Task<DeleteCharacterResultEnum> DeleteCharacterByAccountAndPositionAsync(
            long accountId, byte characterPosition)
        {
            try
            {
                var dto = await _context.Character
                    .AsNoTrackingWithIdentityResolution()
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
                    .Include(x => x.BuffList).ThenInclude(y => y.Buffs)
                    .Include(x => x.SealList).ThenInclude(y => y.Seals)
                    .Include(x => x.ItemList).ThenInclude(y => y.Items)
                    .Include(x => x.Digimons).ThenInclude(y => y.Digiclone)
                    .Include(x => x.Digimons).ThenInclude(y => y.AttributeExperience)
                    .Include(x => x.Digimons).ThenInclude(y => y.Location)
                    .Include(x => x.Digimons).ThenInclude(y => y.BuffList).ThenInclude(z => z.Buffs)
                    .Include(x => x.Digimons).ThenInclude(z => z.Evolutions)
                    .SingleOrDefaultAsync(x => x.AccountId == accountId && x.Position == characterPosition)
                    .ConfigureAwait(false);

                if (dto != null)
                {
                    _context.Remove(dto);
                    await _context.SaveChangesAsync().ConfigureAwait(false);
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
            var character = await _context.Character
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (character == null) return;

            character.Channel = channel;

            _context.Character.Update(character);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task UpdateCharacterLocationAsync(CharacterLocationModel location)
        {
            var dto = await _context.CharacterLocation.FindAsync(location.Id).ConfigureAwait(false);

            if (dto == null) return;

            dto.MapId = location.MapId;
            dto.X = location.X;
            dto.Y = location.Y;
            dto.Z = location.Z;

            _context.CharacterLocation.Update(dto);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task UpdateDigimonLocationAsync(DigimonLocationModel location)
        {
            var dto = await _context.DigimonLocation.FindAsync(location.Id).ConfigureAwait(false);

            if (dto == null) return;

            dto.MapId = location.MapId;
            dto.X = location.X;
            dto.Y = location.Y;
            dto.Z = location.Z;

            _context.DigimonLocation.Update(dto);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task UpdateCharacterResourcesAsync(CharacterModel tamer)
        {
            var tamerDto = await _context.Character
                .Include(x => x.Digimons)
                .FirstOrDefaultAsync(x => x.Id == tamer.Id);

            if (tamerDto == null) return;

            tamerDto.CurrentHp = tamer.CurrentHp;
            tamerDto.CurrentDs = tamer.CurrentDs;

            if (tamer.Digimons?.Any() == true)
            {
                // Atualiza apenas os digimons presentes no payload, sem substituir a coleção
                foreach (var dModel in tamer.Digimons)
                {
                    var dDto = tamerDto.Digimons.FirstOrDefault(x => x.Id == dModel.Id);
                    if (dDto != null)
                    {
                        dDto.CurrentHp = dModel.CurrentHp;
                        dDto.CurrentDs = dModel.CurrentDs;

                        // Se o cliente enviar CurrentType durante o tick, atualiza também
                        if (!Equals(dModel.CurrentType, default(int))) // ajuste se for enum/nullable
                            dDto.CurrentType = dModel.CurrentType;

                        _context.Digimon.Update(dDto);
                    }
                }
            }

            // ⚠️ Não usar _context.Update(tamerDto) aqui para não mexer na coleção; os objetos já estão tracked
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCharactersStateAsync(CharacterStateEnum state)
        {
            var characters = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .ToListAsync()
                .ConfigureAwait(false);

            characters.ForEach(character =>
            {
                character.State = state;
                character.EventState = CharacterEventStateEnum.None;
            });

            _context.Character.UpdateRange(characters);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task UpdateCharacterStateByIdAsync(long characterId, CharacterStateEnum state)
        {
            var character = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (character != null)
            {
                character.State = state;

                _context.Character.Update(character);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterExperienceAsync(long tamerId, long currentExperience, byte level)
        {
            var dto = await _context.Character.FindAsync(tamerId).ConfigureAwait(false);

            if (dto == null) return;

            dto.CurrentExperience = currentExperience;
            dto.Level = level;

            _context.Character.Update(dto);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task UpdateDigimonExperienceAsync(DigimonModel digimon)
        {
            var dto = await _context.Digimon
                .AsNoTrackingWithIdentityResolution()
                .Include(x => x.Evolutions)
                .Include(x => x.AttributeExperience)
                .FirstOrDefaultAsync(x => x.Id == digimon.Id)
                .ConfigureAwait(false);

            if (dto == null) return;

            dto.CurrentExperience = digimon.CurrentExperience;
            dto.CurrentSkillExperience = digimon.CurrentSkillExperience;
            dto.TranscendenceExperience = digimon.TranscendenceExperience;
            dto.Level = digimon.Level;

            var attributeExperience = digimon.AttributeExperience;
            var dtoAttributeExperience = dto.AttributeExperience;

            dtoAttributeExperience.Data = attributeExperience.Data;
            dtoAttributeExperience.Vaccine = attributeExperience.Vaccine;
            dtoAttributeExperience.Virus = attributeExperience.Virus;
            dtoAttributeExperience.Ice = attributeExperience.Ice;
            dtoAttributeExperience.Water = attributeExperience.Water;
            dtoAttributeExperience.Fire = attributeExperience.Fire;
            dtoAttributeExperience.Land = attributeExperience.Land;
            dtoAttributeExperience.Wind = attributeExperience.Wind;
            dtoAttributeExperience.Wood = attributeExperience.Wood;
            dtoAttributeExperience.Light = attributeExperience.Light;
            dtoAttributeExperience.Dark = attributeExperience.Dark;
            dtoAttributeExperience.Thunder = attributeExperience.Thunder;
            dtoAttributeExperience.Steel = attributeExperience.Steel;

            foreach (var evolutionDto in dto.Evolutions)
            {
                var evolutionModel = digimon.Evolutions.FirstOrDefault(x => x.Id == evolutionDto.Id);
                if (evolutionModel == null) continue;

                evolutionDto.Type = evolutionModel.Type;
                evolutionDto.Unlocked = evolutionModel.Unlocked;
                evolutionDto.SkillPoints = evolutionModel.SkillPoints;
                evolutionDto.SkillMastery = evolutionModel.SkillMastery;
                evolutionDto.SkillExperience = evolutionModel.SkillExperience;
            }

            _context.Update(dto);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task UpdateCharacterSealsAsync(CharacterSealListModel sealList)
        {
            var dto = await _context.CharacterSealList
                .AsNoTrackingWithIdentityResolution()
                .Include(x => x.Seals)
                .FirstOrDefaultAsync(x => x.Id == sealList.Id)
                .ConfigureAwait(false);

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
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task AddChatMessageAsync(ChatMessageModel chatMessage)
        {
            var dto = _mapper.Map<ChatMessageDTO>(chatMessage);
            if (dto != null)
            {
                _context.Add(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdatePartnerCurrentTypeAsync(DigimonModel digimon)
        {
            var dto = await _context.Digimon
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == digimon.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.CurrentType = digimon.CurrentType;

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateDigicloneAsync(DigimonDigicloneModel digiclone)
        {
            var dto = await _context.DigimonDigiclone
                .AsNoTrackingWithIdentityResolution()
                .Include(x => x.History)
                .FirstOrDefaultAsync(x => x.Id == digiclone.Id || x.DigimonId == digiclone.DigimonId)
                .ConfigureAwait(false);

            if (dto != null)
            {
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

                dto.History.ATValues = digiclone.History.ATValues;
                dto.History.BLValues = digiclone.History.BLValues;
                dto.History.CTValues = digiclone.History.CTValues;
                dto.History.EVValues = digiclone.History.EVValues;
                dto.History.HPValues = digiclone.History.HPValues;

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterTitleByIdAsync(long characterId, short titleId)
        {
            var dto = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.CurrentTitle = titleId;

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterProgressCompleteAsync(CharacterProgressModel progress)
        {
            var dto = await _context.CharacterProgress
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == progress.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.CompletedData = progress.CompletedData;
                dto.CompletedDataValue = progress.CompletedDataValue;

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        public async Task UpdateCharacterBuffListAsync(CharacterBuffListModel buffList)
        {
            var dto = await _context.CharacterBuffList
                .Include(x => x.Buffs)
                .FirstOrDefaultAsync(x => x.Id == buffList.Id)
                .ConfigureAwait(false);

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
                    _context.Update(existingBuff);
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
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                Console.WriteLine($"[WARN] Concurrency conflict on buffs update: {ex.Message}");
            }
        }

        public async Task UpdateDigimonBuffListAsync(DigimonBuffListModel buffList)
        {
            var dto = await _context.DigimonBuffList
                .AsNoTrackingWithIdentityResolution()
                .Include(x => x.Buffs)
                .FirstOrDefaultAsync(x => x.Id == buffList.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                var buffsToRemove = dto.Buffs
                    .Where(dtoBuff => !buffList.Buffs.Any(buff => buff.Id == dtoBuff.Id))
                    .ToList();

                foreach (var buffToRemove in buffsToRemove)
                {
                    dto.Buffs.Remove(buffToRemove);
                    _context.Remove(buffToRemove);
                    await _context.SaveChangesAsync().ConfigureAwait(false);
                }

                foreach (var buff in buffList.Buffs)
                {
                    var dtoBuff = dto.Buffs.FirstOrDefault(x => x.Id == buff.Id);
                    if (dtoBuff != null)
                    {
                        dtoBuff.Duration = buff.Duration;
                        dtoBuff.EndDate = buff.EndDate;
                        dtoBuff.SkillId = buff.SkillId;
                        dtoBuff.TypeN = buff.TypeN;
                        dtoBuff.CoolEndDate = buff.CoolEndDate;
                        dtoBuff.Cooldown = buff.Cooldown;
                        _context.Update(dtoBuff);
                        await _context.SaveChangesAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        dtoBuff = _mapper.Map<DigimonBuffDTO>(buff);
                        dtoBuff.BuffListId = buffList.Id;
                        dto.Buffs.Add(dtoBuff);
                        _context.Add(dtoBuff);
                        await _context.SaveChangesAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        public async Task UpdateCharacterActiveEvolutionAsync(CharacterActiveEvolutionModel activeEvolution)
        {
            var dto = await _context.CharacterActiveEvolution.FindAsync(activeEvolution.Id).ConfigureAwait(false);

            if (dto == null) return;

            dto.XgPerSecond = activeEvolution.XgPerSecond;
            dto.DsPerSecond = activeEvolution.DsPerSecond;

            _context.CharacterActiveEvolution.Update(dto);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task UpdateCharacterBasicInfoAsync(CharacterModel character)
        {
            var dto = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .Include(x => x.Digimons)
                .SingleOrDefaultAsync(x => x.Id == character.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.CurrentHp = character.CurrentHp;
                dto.CurrentDs = character.CurrentDs;
                dto.XGauge = character.XGauge;
                dto.XCrystals = character.XCrystals;

                foreach (var digimonDto in dto.Digimons)
                {
                    var digimonModel = character.Digimons
                        .FirstOrDefault(x => x.Id == digimonDto.Id);

                    if (digimonModel != null)
                    {
                        digimonDto.CurrentHp = digimonModel.CurrentHp;
                        digimonDto.CurrentDs = digimonModel.CurrentDs;
                        digimonDto.CurrentType = digimonModel.CurrentType;
                    }
                }

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateItemListBitsAsync(long itemListId, long bits)
        {
            var dto = await _context.ItemLists
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == itemListId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.Bits = bits;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        public async Task UpdateItemsAsync(List<ItemModel> items)
        {
            await RemoveDeletedItems(items).ConfigureAwait(false);
            await AddOrUpdateItems(items).ConfigureAwait(false);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        private async Task AddOrUpdateItems(List<ItemModel> items)
        {
            if (!items.Any()) return;

            foreach (var item in items.ToList())
            {
                var dto = await _context.Items
                    .Include(x => x.AccessoryStatus)
                    .Include(x => x.SocketStatus)
                    .FirstOrDefaultAsync(x => x.Id == item.Id)
                    .ConfigureAwait(false);

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

                    // Proteções: só mapeia status se existirem do lado do model
                    if (item.AccessoryStatus != null && dto.AccessoryStatus != null)
                    {
                        foreach (var dtoStatus in dto.AccessoryStatus)
                        {
                            var modelStatus = item.AccessoryStatus.FirstOrDefault(x => x.Slot == dtoStatus.Slot);
                            if (modelStatus != null)
                            {
                                dtoStatus.Type = modelStatus.Type;
                                dtoStatus.Value = modelStatus.Value;
                            }
                        }
                    }

                    if (item.SocketStatus != null && dto.SocketStatus != null)
                    {
                        foreach (var dtoStatus in dto.SocketStatus)
                        {
                            var modelStatus = item.SocketStatus.FirstOrDefault(x => x.Slot == dtoStatus.Slot);
                            if (modelStatus != null)
                            {
                                dtoStatus.Type = modelStatus.Type;
                                dtoStatus.AttributeId = modelStatus.AttributeId;
                                dtoStatus.Value = modelStatus.Value;
                            }
                        }
                    }
                }
                else
                {
                    await _context.AddAsync(_mapper.Map<ItemDTO>(item)).ConfigureAwait(false);
                }
            }
        }
        private async Task RemoveDeletedItems(List<ItemModel> items)
        {
            if (!items.Any())
                return;

            var itemListId = items.First().ItemListId;

            // Se todos os itens recebidos são "novos" (Id == Guid.Empty),
            // interpretamos como DELTA de adição e NÃO removemos nada
            var looksLikeAddDeltaOnly = items.All(i => i.Id == Guid.Empty);
            if (looksLikeAddDeltaOnly)
                return;

            var existing = await _context.Items
                .AsNoTracking()
                .Where(x => x.ItemListId == itemListId)
                .ToListAsync()
                .ConfigureAwait(false);

            // Considera apenas IDs válidos vindos do model
            var incomingIds = items.Where(i => i.Id != Guid.Empty).Select(i => i.Id).ToHashSet();

            // Remover apenas os que claramente não estão mais na lista recebida
            var toRemove = existing.Where(x => x.Id != Guid.Empty && !incomingIds.Contains(x.Id)).ToList();

            foreach (var rem in toRemove)
                _context.Remove(rem);
        }

        public async Task UpdateItemAccessoryStatusAsync(ItemModel item)
        {
            var dto = await _context.Items
                .AsNoTracking()
                .Include(x => x.AccessoryStatus)
                .FirstOrDefaultAsync(x => x.Id == item.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.RerollLeft = item.RerollLeft;
                dto.Power = item.Power;

                if (item.AccessoryStatus != null && dto.AccessoryStatus != null)
                {
                    foreach (var dtoStatus in dto.AccessoryStatus)
                    {
                        var modelStatus = item.AccessoryStatus.FirstOrDefault(x => x.Slot == dtoStatus.Slot);
                        if (modelStatus != null)
                        {
                            dtoStatus.Type = modelStatus.Type;
                            dtoStatus.Value = modelStatus.Value;
                        }
                    }
                }

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        public async Task UpdateItemSocketStatusAsync(ItemModel item)
        {
            var dto = await _context.Items
                .AsNoTracking()
                .Include(x => x.SocketStatus)
                .FirstOrDefaultAsync(x => x.Id == item.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.RerollLeft = item.RerollLeft;
                dto.Power = item.Power;

                if (item.SocketStatus != null && dto.SocketStatus != null)
                {
                    foreach (var dtoStatus in dto.SocketStatus)
                    {
                        var modelStatus = item.SocketStatus.FirstOrDefault(x => x.Slot == dtoStatus.Slot);
                        if (modelStatus != null)
                        {
                            dtoStatus.Type = modelStatus.Type;
                            dtoStatus.AttributeId = modelStatus.AttributeId;
                            dtoStatus.Value = modelStatus.Value;
                        }
                    }
                }

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        public async Task UpdateItemAsync(ItemModel item)
        {
            var dto = await _context.Items
                .Include(x => x.AccessoryStatus)
                .Include(x => x.SocketStatus)
                .FirstOrDefaultAsync(x => x.Id == item.Id)
                .ConfigureAwait(false);

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

                if (item.AccessoryStatus != null && dto.AccessoryStatus != null)
                {
                    foreach (var dtoStatus in dto.AccessoryStatus)
                    {
                        var modelStatus = item.AccessoryStatus.FirstOrDefault(x => x.Slot == dtoStatus.Slot);
                        if (modelStatus != null)
                        {
                            dtoStatus.Type = modelStatus.Type;
                            dtoStatus.Value = modelStatus.Value;
                        }
                    }
                }

                if (item.SocketStatus != null && dto.SocketStatus != null)
                {
                    foreach (var dtoStatus in dto.SocketStatus)
                    {
                        var modelStatus = item.SocketStatus.FirstOrDefault(x => x.Slot == dtoStatus.Slot);
                        if (modelStatus != null)
                        {
                            dtoStatus.Type = modelStatus.Type;
                            dtoStatus.AttributeId = modelStatus.AttributeId;
                            dtoStatus.Value = modelStatus.Value;
                        }
                    }
                }

                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        public async Task UpdateItemListSizeAsync(long itemListId, short newSize, CancellationToken ct = default)
        {
            var dto = await _context.ItemLists
                .FirstOrDefaultAsync(x => x.Id == itemListId, ct)
                .ConfigureAwait(false);

            if (dto != null)
            {
                // ⚠️ O cliente espera 0–255. Garantimos que não passa de byte.
                dto.Size = (byte)Math.Clamp(newSize, (short)0, (short)255);

                _context.Update(dto);
                await _context.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }

        public async Task AddInventorySlotsAsync(List<ItemModel> items)
        {
            var itemListDto = await _context.ItemLists
                .AsNoTracking()
                .Include(x => x.Items)
                .ThenInclude(y => y.AccessoryStatus)
                .Include(x => x.Items)
                .ThenInclude(y => y.SocketStatus)
                .FirstOrDefaultAsync(x => x.Id == items.First().ItemListId)
                .ConfigureAwait(false);

            if (itemListDto != null)
            {
                foreach (var item in items)
                {
                    _context.Add(_mapper.Map<ItemDTO>(item));
                    itemListDto.Size = (byte)Math.Clamp(itemListDto.Size + 1, 0, 255);
                }

                _context.Update(itemListDto);
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);
        }
        public async Task UpdateCharacterEventStateByIdAsync(long characterId, CharacterEventStateEnum state)
        {
            var dto = await _context.Character
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.EventState = state;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateEvolutionAsync(DigimonEvolutionModel evolution)
        {
            var dto = await _context.DigimonEvolution
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == evolution.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.Type = evolution.Type;
                dto.Unlocked = evolution.Unlocked;
                dto.SkillPoints = evolution.SkillPoints;
                dto.SkillMastery = evolution.SkillMastery;
                dto.SkillExperience = evolution.SkillExperience;

                dto.Skills = _mapper.Map<List<DigimonEvolutionSkillDTO>>(evolution.Skills);

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateIncubatorAsync(CharacterIncubatorModel incubator)
        {
            var dto = await _context.CharacterIncubator
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == incubator.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.EggId = incubator.EggId;
                dto.HatchLevel = incubator.HatchLevel;
                dto.BackupDiskId = incubator.BackupDiskId;

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterMapRegionAsync(CharacterMapRegionModel mapRegion)
        {
            var dto = await _context.CharacterMapRegion
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == mapRegion.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.Unlocked = mapRegion.Unlocked;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
        public async Task UpdateDigimonSizeAsync(long digimonId, short size)
        {
            var dto = await _context.Digimon
                .FindAsync(digimonId)
                .ConfigureAwait(false);

            if (dto == null) return;

            dto.Size = size;

            _context.Digimon.Update(dto);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task UpdateCharacterInitialPacketSentOnceSentAsync(long characterId, bool sendOnceSent)
        {
            var dto = await _context.Character
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (dto == null) return;

            dto.InitialPacketSentOnceSent = sendOnceSent;

            _context.Character.Update(dto);
            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task UpdateCharacterSizeAsync(long characterId, short size)
        {
            var dto = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.Size = size;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateDigimonGradeAsync(long digimonId, DigimonHatchGradeEnum grade)
        {
            var dto = await _context.Digimon
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == digimonId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.HatchGrade = grade;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterDigimonsOrderAsync(CharacterModel character)
        {
            foreach (var digimon in character.Digimons)
            {
                var dto = await _context.Digimon
                    .AsNoTrackingWithIdentityResolution()
                    .FirstOrDefaultAsync(x => x.Id == digimon.Id)
                    .ConfigureAwait(false);

                if (dto != null)
                {
                    dto.Slot = digimon.Slot;
                    _context.Update(dto);
                }
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);
        }

        public async Task DeleteDigimonAsync(long digimonId)
        {
            var dto = await _context.Digimon
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == digimonId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                _context.Remove(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterDigimonArchiveItemAsync(CharacterDigimonArchiveItemModel characterDigimonArchiveItem)
        {
            var dto = await _context.CharacterDigimonArchiveItem
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(x => x.Id == characterDigimonArchiveItem.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.DigimonId = characterDigimonArchiveItem.DigimonId;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateDigimonSlotAsync(long digimonId, byte digimonSlot)
        {
            var dto = await _context.Digimon
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(x => x.Id == digimonId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.Slot = digimonSlot;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterXaiAsync(CharacterXaiModel xai)
        {
            var dto = await _context.CharacterXai
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(x => x.Id == xai.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.ItemId = xai.ItemId;
                dto.XCrystals = xai.XCrystals;
                dto.XGauge = xai.XGauge;

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task AddDigimonArchiveSlotAsync(Guid archiveId, CharacterDigimonArchiveItemModel archiveItem)
        {
            var archiveDto = await _context.CharacterDigimonArchive
                .AsNoTrackingWithIdentityResolution()
                .Include(x => x.DigimonArchives)
                .SingleOrDefaultAsync(x => x.Id == archiveId)
                .ConfigureAwait(false);

            if (archiveDto != null)
            {
                var dto = _mapper.Map<CharacterDigimonArchiveItemDTO>(archiveItem);
                dto.DigimonArchiveId = archiveId;
                _context.CharacterDigimonArchiveItem.Add(dto);

                archiveDto.Slots++;
                _context.Update(archiveDto);

                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterDigimonSlotsAsync(long characterId, byte slots)
        {
            var characterDto = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (characterDto != null)
            {
                characterDto.DigimonSlots = slots;
                _context.Update(characterDto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task<CharacterDTO> ChangeCharacterNameAsync(long characterId, string NewCharacterName)
        {
            var dto = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.Name = NewCharacterName;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }

            return dto;
        }

        public async Task<CharacterDTO> ChangeCharacterIdTpAsync(long characterId, int TargetTamerIdTP)
        {
            var dto = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.TargetTamerIdTP = TargetTamerIdTP;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }

            return dto;
        }

        public async Task<DigimonDTO> ChangeDigimonNameAsync(long digimonId, string NewDigimonName)
        {
            var dto = await _context.Digimon
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == digimonId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.Name = NewDigimonName;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }

            return dto;
        }

        public async Task<CharacterDTO> ChangeTamerModelAsync(long characterId, CharacterModelEnum model)
        {
            var dto = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.Model = model;
                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }

            return dto;
        }
        public async Task UpdateTamerSkillCooldownAsync(CharacterTamerSkillModel activeSkill)
        {
            var dto = await _context.ActiveSkills
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == activeSkill.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.SkillId = activeSkill.SkillId;
                dto.Cooldown = activeSkill.Cooldown;
                dto.EndCooldown = activeSkill.EndCooldown;
                dto.Type = activeSkill.Type;
                dto.Duration = activeSkill.Duration;
                dto.EndDate = activeSkill.EndDate;

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task AddInventorySlotAsync(ItemModel newSlot)
        {
            var itemListDto = await _context.ItemLists
                .AsNoTracking()
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == newSlot.ItemListId)
                .ConfigureAwait(false);

            if (itemListDto != null)
            {
                var dto = _mapper.Map<ItemDTO>(newSlot);
                _context.Add(dto);

                itemListDto.Size = (byte)Math.Clamp(itemListDto.Size + 1, 0, 255);
                _context.Update(itemListDto);
            }

            await _context.SaveChangesAsync().ConfigureAwait(false);
        }
        public async Task UpdateCharacterArenaPointsAsync(CharacterArenaPointsModel points)
        {
            var dto = await _context.CharacterPoints
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == points.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.CurrentStage = points.CurrentStage;
                dto.Amount = points.Amount;
                dto.ItemId = points.ItemId;

                _context.CharacterPoints.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterInProgressAsync(InProgressQuestModel progress)
        {
            var dto = await _context.InProgressQuest
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == progress.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.FirstCondition = progress.FirstCondition;
                dto.SecondCondition = progress.SecondCondition;
                dto.ThirdCondition = progress.ThirdCondition;
                dto.FourthCondition = progress.FourthCondition;
                dto.FifthCondition = progress.FifthCondition;

                _context.InProgressQuest.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task AddCharacterProgressAsync(CharacterProgressModel progress)
        {
            var dto = await _context.CharacterProgress
                .Include(x => x.InProgressQuestData)
                .FirstOrDefaultAsync(x => x.Id == progress.Id)
                .ConfigureAwait(false);

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
                }

                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateTamerAttendanceRewardAsync(AttendanceRewardModel attendanceRewardModel)
        {
            var dto = await _context.AttendanceReward
                .FirstOrDefaultAsync(x => x.CharacterId == attendanceRewardModel.CharacterId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                if (dto.LastRewardDate.Month != DateTime.Now.Month)
                {
                    dto.TotalDays = 0;
                }

                dto.LastRewardDate = attendanceRewardModel.LastRewardDate;
                dto.TotalDays = attendanceRewardModel.TotalDays;

                _context.AttendanceReward.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterDeckBuffAsync(CharacterModel character)
        {
            var dto = await _context.Character
                .Include(x => x.DeckBuff)
                    .ThenInclude(x => x.Options)
                    .ThenInclude(x => x.DeckBookInfo)
                .FirstOrDefaultAsync(x => x.Id == character.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.DeckBuffId = character.DeckBuffId;

                _context.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateTamerTimeRewardAsync(TimeRewardModel timeRewardModel)
        {
            var dto = await _context.TimeReward
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.CharacterId == timeRewardModel.CharacterId)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.StartTime = timeRewardModel.StartTime;
                dto.RewardIndex = timeRewardModel.RewardIndex;
                dto.AtualTime = timeRewardModel.AtualTime;

                _context.TimeReward.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterArenaDailyPointsAsync(CharacterArenaDailyPointsModel points)
        {
            var dto = await _context.CharacterDailyPoints
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == points.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.InsertDate = points.InsertDate;
                dto.Points = points.Points;

                _context.CharacterDailyPoints.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task<CharacterEncyclopediaModel> CreateCharacterEncyclopediaAsync(CharacterEncyclopediaModel characterEncyclopedia)
        {
            var tamerDto = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .Include(x => x.Encyclopedia)
                .ThenInclude(x => x.Evolutions)
                .SingleOrDefaultAsync(x => x.Id == characterEncyclopedia.CharacterId)
                .ConfigureAwait(false);

            var dto = _mapper.Map<CharacterEncyclopediaDTO>(characterEncyclopedia);

            if (tamerDto != null)
            {
                try
                {
                    tamerDto.Encyclopedia.Add(dto);

                    _context.Update(tamerDto);
                    await _context.SaveChangesAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                }
            }

            return _mapper.Map<CharacterEncyclopediaModel>(dto);
        }

        public async Task UpdateCharacterEncyclopediaAsync(CharacterEncyclopediaModel characterEncyclopedia)
        {
            var dto = await _context.CharacterEncyclopedia
                .Include(x => x.Evolutions)
                .SingleOrDefaultAsync(x => x.Id == characterEncyclopedia.Id)
                .ConfigureAwait(false);

            try
            {
                if (dto != null)
                {
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

                    // 🔹 Atualizar evoluções uma a uma
                    foreach (var evoModel in characterEncyclopedia.Evolutions)
                    {
                        var existing = dto.Evolutions.FirstOrDefault(e => e.Id == evoModel.Id);
                        if (existing != null)
                        {
                            // Atualiza existente
                            _mapper.Map(evoModel, existing);
                        }
                        else
                        {
                            // Adiciona nova evolução
                            var newEvo = _mapper.Map<CharacterEncyclopediaEvolutionsDTO>(evoModel);
                            dto.Evolutions.Add(newEvo);
                        }
                    }

                    // 🔹 Remover evoluções que não existem mais no Model
                    var toRemove = dto.Evolutions
                        .Where(e => characterEncyclopedia.Evolutions.All(m => m.Id != e.Id))
                        .ToList();
                    foreach (var r in toRemove)
                        dto.Evolutions.Remove(r);

                    _context.CharacterEncyclopedia.Update(dto);
                    await _context.SaveChangesAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                throw;
            }
        }

        public async Task UpdateCharacterEncyclopediaEvolutionsAsync(CharacterEncyclopediaEvolutionsModel characterEncyclopediaEvolution)
        {
            var dto = await _context.CharacterEncyclopediaEvolutions
                .AsNoTrackingWithIdentityResolution()
                .FirstOrDefaultAsync(x => x.Id == characterEncyclopediaEvolution.Id)
                .ConfigureAwait(false);

            if (dto != null)
            {
                dto.IsUnlocked = characterEncyclopediaEvolution.IsUnlocked;
                dto.CreateDate = DateTime.Now;

                _context.CharacterEncyclopediaEvolutions.Update(dto);
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }

        public async Task UpdateCharacterFriendsAsync(CharacterModel? character, bool connected = false)
        {
            List<CharacterFriendDTO> dto;
            if (character != null)
            {
                dto = await _context.CharacterFriends
                    .Where(x => x.FriendId == character.Id)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
            else
            {
                dto = await _context.CharacterFriends
                    .ToListAsync()
                    .ConfigureAwait(false);
            }

            if (!dto.IsNullOrEmpty())
            {
                dto.ForEach(friend => friend.SetConnected(connected));
                await _context.SaveChangesAsync().ConfigureAwait(false);
            }
        }
    }
}
