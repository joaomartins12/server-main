using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Commons.DTOs.Character;
using DigitalWorldOnline.Commons.DTOs.Digimon;
using DigitalWorldOnline.Commons.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DigitalWorldOnline.Infrastructure.Repositories.Character
{
    public class CharacterQueriesRepository : ICharacterQueriesRepository
    {
        private readonly DatabaseContext _context;

        public CharacterQueriesRepository(DatabaseContext context)
        {
            _context = context;
        }

        public async Task<IDictionary<byte, byte>> GetChannelsByMapIdAsync(short mapId)
        {
            var channels = new Dictionary<byte, byte>
            {
                { 0, 0 },
                { 1, 0 },
                { 2, 0 }
            };

            var tamersChannel = await _context.Character
                .AsNoTracking()
                .Where(x => x.Location.MapId == mapId && x.Channel != byte.MaxValue)
                .Select(x => x.Channel)
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var tamerChannel in tamersChannel)
            {
                if (!channels.ContainsKey(tamerChannel))
                    channels.Add(tamerChannel, 1);
                else
                    channels[tamerChannel]++;
            }

            return channels;
        }

        public async Task<CharacterDTO?> GetCharacterAndItemsByIdAsync(long characterId)
        {
            var dto = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .AsSplitQuery()
                .Include(x => x.ItemList).ThenInclude(y => y.Items).ThenInclude(z => z.SocketStatus)
                .Include(x => x.ItemList).ThenInclude(y => y.Items).ThenInclude(z => z.AccessoryStatus)
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            dto?.ItemList.ForEach(itemList =>
                itemList.Items = itemList.Items.OrderBy(x => x.Slot).ToList());

            return dto;
        }

        public async Task<CharacterDTO?> GetCharacterByAccountIdAndPositionAsync(long accountId, byte position)
        {
            return await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.Digimons)
                .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Position == position)
                .ConfigureAwait(false);
        }

        public async Task<CharacterDTO?> GetCharacterByIdAsync(long characterId)
        {
            var character = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .AsSplitQuery()
                .Include(x => x.ActiveEvolution)
                .Include(x => x.Incubator)
                .Include(x => x.Location)
                .Include(x => x.Xai)
                .Include(x => x.TimeReward)
                .Include(x => x.AttendanceReward)
                .Include(x => x.ActiveSkill)
                .Include(x => x.DailyPoints)
                .Include(x => x.Friends).ThenInclude(y => y.Friend)
                .Include(x => x.Friended).ThenInclude(y => y.Character)
                .Include(x => x.Foes).ThenInclude(y => y.Foe)
                .Include(x => x.Foed).ThenInclude(y => y.Character)
                .Include(x => x.Encyclopedia).ThenInclude(y => y.Evolutions)
                .Include(x => x.Encyclopedia).ThenInclude(y => y.EvolutionAsset)
                .Include(x => x.ConsignedShop).ThenInclude(y => y.Location)
                .Include(x => x.Progress).ThenInclude(x => x.InProgressQuestData)
                .Include(y => y.MapRegions)
                .Include(x => x.Points)
                .Include(x => x.BuffList).ThenInclude(y => y.Buffs)
                .Include(x => x.SealList).ThenInclude(y => y.Seals)
                .Include(x => x.DigimonArchive).ThenInclude(y => y.DigimonArchives)
                .Include(x => x.ItemList).ThenInclude(y => y.Items).ThenInclude(z => z.SocketStatus)
                .Include(x => x.ItemList).ThenInclude(y => y.Items).ThenInclude(z => z.AccessoryStatus)
                .Include(x => x.Digimons).ThenInclude(y => y.Digiclone).ThenInclude(z => z.History)
                .Include(x => x.Digimons).ThenInclude(y => y.AttributeExperience)
                .Include(x => x.Digimons).ThenInclude(y => y.Location)
                .Include(x => x.Digimons).ThenInclude(y => y.BuffList).ThenInclude(z => z.Buffs)
                .Include(x => x.Digimons).ThenInclude(y => y.Evolutions).ThenInclude(z => z.Skills)
                .Include(x => x.DeckBuff).ThenInclude(y => y.Options).ThenInclude(z => z.DeckBookInfo)
                .SingleOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            if (character != null)
            {
                character.ItemList.ForEach(itemList => itemList.Items = itemList.Items.OrderBy(x => x.Slot).ToList());
                character.Digimons = character.Digimons.Where(x => x.Slot <= 5).OrderBy(x => x.Slot).ToList();
            }

            return character;
        }

        public async Task<CharacterDTO?> GetCharacterByNameAsync(string characterName)
        {
            return await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Name == characterName)
                .ConfigureAwait(false);
        }

        public async Task<DigimonDTO?> GetDigimonByIdAsync(long digimonId)
        {
            return await _context.Digimon
                .AsNoTrackingWithIdentityResolution()
                .AsSplitQuery()
                .Include(x => x.Digiclone).ThenInclude(x => x.History)
                .Include(x => x.AttributeExperience)
                .Include(x => x.Evolutions).ThenInclude(y => y.Skills)
                .Include(x => x.BuffList).ThenInclude(x => x.Buffs)
                .SingleOrDefaultAsync(x => x.Id == digimonId)
                .ConfigureAwait(false);
        }

        public async Task<List<DigimonDTO>> GetAllDigimonsAsync()
        {
            return await _context.Digimon
                .AsNoTrackingWithIdentityResolution()
                .AsSplitQuery()
                .Include(x => x.Character).ThenInclude(y => y.Encyclopedia).ThenInclude(y => y.Evolutions)
                .Include(x => x.Digiclone).ThenInclude(x => x.History)
                .Include(x => x.AttributeExperience)
                .Include(x => x.Location)
                .Include(x => x.Evolutions).ThenInclude(y => y.Skills)
                .Include(x => x.BuffList).ThenInclude(x => x.Buffs)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<IList<CharacterDTO>> GetCharactersByAccountIdAsync(long accountId)
        {
            var characters = await _context.Character
                .AsNoTrackingWithIdentityResolution()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.Xai)
                .Include(x => x.SealList).ThenInclude(y => y.Seals)
                .Include(x => x.ItemList).ThenInclude(y => y.Items)
                .Include(x => x.Digimons)
                .Where(x => x.AccountId == accountId)
                .ToListAsync()
                .ConfigureAwait(false);

            characters.ForEach(character =>
            {
                character.ItemList.ForEach(itemList =>
                    itemList.Items = itemList.Items.OrderBy(x => x.Slot).ToList());

                character.Digimons = character.Digimons.Where(x => x.Slot <= 5).OrderBy(x => x.Slot).ToList();
            });

            return characters;
        }

        public async Task<(string TamerName, string GuildName)> GetCharacterNameAndGuildByIdQAsync(long characterId)
        {
            var dto = await _context.Character
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == characterId)
                .ConfigureAwait(false);

            var dtoGuild = await _context.Guild
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Members.Any(m => m.CharacterId == characterId))
                .ConfigureAwait(false);

            if (dto != null && dtoGuild == null)
                return (dto.Name, string.Empty);

            if (dto != null && dtoGuild != null)
                return (dto.Name, dtoGuild.Name);

            return (string.Empty, string.Empty);
        }

        public async Task<IList<CharacterEncyclopediaDTO>> GetCharacterEncyclopediaByCharacterIdAsync(long characterId)
        {
            return await _context.CharacterEncyclopedia
                .AsNoTrackingWithIdentityResolution()
                .AsSplitQuery()
                .Include(x => x.Evolutions)
                .Include(x => x.EvolutionAsset)
                .Where(x => x.CharacterId == characterId)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task<DigimonBaseInfoAssetDTO?> GetBaseInfoByTypeAsync(int type)
        {
            return await _context.DigimonBaseInfoAsset
                .AsNoTracking()
                .Where(x => x.Type == type)
                .Select(x => new DigimonBaseInfoAssetDTO
                {
                    Id = x.Id,
                    Type = x.Type,
                    Model = x.Model,
                    Name = x.Name,
                    Level = x.Level,
                    ScaleType = x.ScaleType,
                    EvolutionType = x.EvolutionType,
                    Attribute = x.Attribute,
                    Element = x.Element,
                    Family1 = x.Family1,
                    Family2 = x.Family2,
                    Family3 = x.Family3
                })
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);
        }
    }
}
