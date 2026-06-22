using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Application.Admin.Repositories;
using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Commons.DTOs.Config;
using DigitalWorldOnline.Commons.Enums.Admin;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.ViewModel.Summons;
using System.Linq;

namespace DigitalWorldOnline.Infrastructure.Repositories.Admin
{
    public class AdminQueriesRepository : IAdminQueriesRepository
    {
        private readonly DatabaseContext _context;

        public AdminQueriesRepository(DatabaseContext context)
        {
            _context = context;
        }

        public async Task<GetSummonByIdQueryDto> GetSummonByIdAsync(long id)
        {
            var result = new GetSummonByIdQueryDto
            {
                Register = await _context.SummonsConfig
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id)
            };

            return result;
        }

        public async Task<GetAccountByIdQueryDto> GetAccountByIdAsync(long id)
        {
            var result = new GetAccountByIdQueryDto
            {
                Register = await _context.Account
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(a => a.AccountBlock) // carregar dados de banimento
                    .SingleOrDefaultAsync(x => x.Id == id)
            };

            return result;
        }

        public async Task<GetAccountsQueryDto> GetAccountsAsync(
            int limit,
            int offset,
            string sortColumn,
            SortDirectionEnum sortDirection,
            string? filter)
        {
            var result = new GetAccountsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (filter?.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Desc;

            var query = _context.Account.AsNoTracking();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x => x.Username.Contains(filter) || x.Email.Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {sortDirection}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetMapsQueryDto> GetMapsAsync(
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection,
    string? filter)
        {
            var result = new GetMapsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (filter?.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Asc;

            var query = _context.MapConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Mobs)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Name.Contains(filter) ||
                    x.MapId.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {sortDirection}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetSummonMobsQueryDto> GetSummonMobsAsync(
    long mapId,
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection,
    string? filter)
        {
            var result = new GetSummonMobsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Desc;

            var query = _context.SummonsMobConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward)
                .Where(x => x.SummonDTOId == mapId);

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Name.Contains(filter) ||
                    x.Type.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {sortDirection}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetMobsQueryDto> GetMobsAsync(
            long mapId,
            int limit,
            int offset,
            string sortColumn,
            SortDirectionEnum sortDirection,
            string? filter)
        {
            var result = new GetMobsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (filter?.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Desc;

            var query = _context.MobConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward).ThenInclude(y => y.Drops)
                .Include(x => x.DropReward).ThenInclude(y => y.BitsDrop)
                .Where(x => x.GameMapConfigId == mapId);

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Name.Contains(filter) ||
                    x.Type.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {sortDirection}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetRaidsQueryDto> GetRaidsAsync(
    long mapId,
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection,
    string? filter)
        {
            var result = new GetRaidsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (filter?.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Desc;

            var query = _context.MobConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward).ThenInclude(y => y.Drops)
                .Include(x => x.DropReward).ThenInclude(y => y.BitsDrop)
                .Where(x => x.GameMapConfigId == mapId && x.Class == 8);

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Name.Contains(filter) ||
                    x.Type.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {sortDirection}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetMapByIdQueryDto> GetMapByIdAsync(long id)
        {
            var result = new GetMapByIdQueryDto
            {
                Register = await _context.MapConfig
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id)
            };

            return result;
        }

        public async Task<GetServerByIdQueryDto> GetServerByIdAsync(long id)
        {
            var result = new GetServerByIdQueryDto
            {
                Register = await _context.ServerConfig
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id)
            };

            return result;
        }

        public async Task<GetServersQueryDto> GetServersAsync(
            int limit,
            int offset,
            string sortColumn,
            SortDirectionEnum sortDirection,
            string? filter)
        {
            var result = new GetServersQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (filter?.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Desc;

            var query = _context.ServerConfig.AsNoTracking();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x => x.Name.Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {sortDirection}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetUserByIdQueryDto> GetUserByIdAsync(long id)
        {
            var result = new GetUserByIdQueryDto
            {
                Register = await _context.UserConfig
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id)
            };

            return result;
        }

        public async Task<GetUsersQueryDto> GetUsersAsync(
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection,
    string filter)
        {
            var result = new GetUsersQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (filter?.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Desc;

            var query = _context.UserConfig.AsNoTracking();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x => x.Username.Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {sortDirection}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetSummonsQueryDto> GetSummonsAsync(
            int limit,
            int offset,
            string sortColumn,
            SortDirectionEnum sortDirection,
            string filter)
        {
            var result = new GetSummonsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (filter?.Length < 2)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Desc;

            var query = _context.SummonsConfig.AsNoTracking();

            if (!string.IsNullOrEmpty(filter) && int.TryParse(filter, out int filterValue))
            {
                query = query.Where(s => s.Maps.Contains(filterValue));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {sortDirection}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetMobByIdQueryDto> GetMobByIdAsync(long id)
        {
            var result = new GetMobByIdQueryDto
            {
                Register = await _context.MobConfig
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(x => x.Location)
                    .Include(x => x.ExpReward)
                    .Include(x => x.DropReward).ThenInclude(y => y.Drops)
                    .Include(x => x.DropReward).ThenInclude(y => y.BitsDrop)
                    .SingleOrDefaultAsync(x => x.Id == id)
            };

            return result;
        }

        public async Task<GetSummonMobByIdQueryDto> GetSummonMobByIdAsync(long id)
        {
            var result = new GetSummonMobByIdQueryDto
            {
                Register = await _context.SummonsMobConfig
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(x => x.Location)
                    .Include(x => x.ExpReward)
                    .Include(x => x.DropReward).ThenInclude(y => y.Drops)
                    .Include(x => x.DropReward).ThenInclude(y => y.BitsDrop)
                    .SingleOrDefaultAsync(x => x.Id == id)
            };

            if (result.Register == null)
            {
                // TODO: Adicionar logging ou exception se necessário
            }

            return result;
        }

        public async Task<GetMobAssetQueryDto> GetMobAssetAsync(string filter)
        {
            var result = new GetMobAssetQueryDto();

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 2)
                filter = string.Empty;

            var query = _context.MonsterBaseInfoAsset.AsNoTracking();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Type.ToString().Contains(filter) ||
                    x.Name.Contains(filter));
            }

            result.Registers = await query.ToListAsync();

            return result;
        }

        public async Task<GetSummonMobAssetQueryDto> GetSummonMobAssetAsync(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 2)
                filter = string.Empty;

            var query = _context.SummonsMobConfig.AsNoTracking();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Type.ToString().Contains(filter) ||
                    x.Name.Contains(filter));
            }

            return new GetSummonMobAssetQueryDto
            {
                Registers = await query.ToListAsync()
            };
        }

        public async Task<GetRaidAssetQueryDto> GetRaidBossAssetAsync(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 2)
                filter = string.Empty;

            var query = _context.MonsterBaseInfoAsset
                .AsNoTracking()
                .Where(x => x.Class == 8);

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Type.ToString().Contains(filter) ||
                    x.Name.Contains(filter));
            }

            return new GetRaidAssetQueryDto
            {
                Registers = await query.ToListAsync()
            };
        }

        public async Task<GetItemAssetQueryDto> GetItemAssetAsync(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 2)
                filter = string.Empty;

            var query = _context.ItemAsset.AsNoTracking();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.ItemId.ToString().Contains(filter) ||
                    x.Name.Contains(filter));
            }

            return new GetItemAssetQueryDto
            {
                Registers = await query.ToListAsync()
            };
        }

        public async Task<GetItemAssetByIdQueryDto> GetItemAssetByIdAsync(int id)
        {
            return new GetItemAssetByIdQueryDto
            {
                Register = await _context.ItemAsset
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.ItemId == id)
            };
        }

        public async Task<GetSpawnPointsQueryDto> GetSpawnPointsAssetAsync(
    int mapId,
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection)
        {
            var result = new GetSpawnPointsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Asc;

            var dto = await _context.MapRegionListAsset
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.MapId == mapId);

            if (dto != null)
            {
                result.TotalRegisters = await _context.MapRegionAsset
                    .AsNoTracking()
                    .CountAsync(x => x.MapRegionListId == dto.Id);

                result.Registers = await _context.MapRegionAsset
                    .AsNoTracking()
                    .Where(x => x.MapRegionListId == dto.Id)
                    .Skip(offset)
                    .Take(limit)
                    .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                    .ToListAsync();
            }
            else
            {
                result.TotalRegisters = 0;
                result.Registers = new List<MapRegionAssetDTO>();
            }

            return result;
        }

        public async Task<GetSpawnPointByIdQueryDto> GetSpawnPointByIdAsync(long id)
        {
            var result = new GetSpawnPointByIdQueryDto();

            var dto = await _context.MapRegionAsset
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.MapRegionList)
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dto != null)
            {
                result.Register = dto;

                var mapDto = await _context.MapConfig
                    .AsNoTracking()
                    .SingleAsync(x => x.MapId == dto.MapRegionList.MapId);

                result.MapId = mapDto.Id;
                result.MapName = mapDto.Name;
            }

            return result;
        }

        public async Task<GetScansQueryDto> GetScansAsync(
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection,
    string? filter)
        {
            var result = new GetScansQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Asc;

            IQueryable<ScanDetailAssetDTO> query = _context.ScanDetail
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Rewards);

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x => x.ItemName.Contains(filter) || x.ItemId.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .Skip(offset)
                .Take(limit)
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .ToListAsync();

            return result;
        }

        public async Task<GetScanByIdQueryDto> GetScanByIdAsync(long id)
        {
            var result = new GetScanByIdQueryDto();

            result.Register = await _context.ScanDetail
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Rewards)
                .SingleOrDefaultAsync(x => x.Id == id);

            return result;
        }

        public async Task<GetContainersQueryDto> GetContainersAsync(
            int limit,
            int offset,
            string sortColumn,
            SortDirectionEnum sortDirection,
            string? filter)
        {
            var result = new GetContainersQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Asc;

            IQueryable<ContainerAssetDTO> query = _context.Container
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Rewards);

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x => x.ItemName.Contains(filter) || x.ItemId.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .Skip(offset)
                .Take(limit)
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .ToListAsync();

            return result;
        }

        public async Task<GetContainerByIdQueryDto> GetContainerByIdAsync(long id)
        {
            return new GetContainerByIdQueryDto
            {
                Register = await _context.Container
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(x => x.Rewards)
                    .SingleOrDefaultAsync(x => x.Id == id)
            };
        }

        public async Task<GetClonsQueryDto> GetClonsAsync(
            int limit,
            int offset,
            string sortColumn,
            SortDirectionEnum sortDirection,
            string? filter)
        {
            var result = new GetClonsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Asc;

            var query = _context.CloneConfig.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filter) && filter.Length >= 2)
            {
                query = query.Where(x =>
                    x.Type.ToString().Contains(filter) ||
                    x.Level.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetClonByIdQueryDto> GetClonByIdAsync(long id)
        {
            return new GetClonByIdQueryDto
            {
                Register = await _context.CloneConfig
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id)
            };
        }

        public async Task<GetGlobalDropsConfigsQueryDto> GetGlobalDropsConfigsAsync(
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection,
    string? filter)
        {
            var result = new GetGlobalDropsConfigsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Asc;

            var query = _context.GlobalDropsConfig.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filter) && filter.Length >= 2)
            {
                if (int.TryParse(filter, out int mapFilter))
                {
                    query = query.Where(x =>
                        x.ItemId.ToString().Contains(filter) ||
                        x.Map == mapFilter);
                }
                else
                {
                    query = query.Where(x =>
                        x.ItemId.ToString().Contains(filter));
                }
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetGlobalDropsConfigByIdQueryDto> GetGlobalDropsConfigByIdAsync(long id)
        {
            return new GetGlobalDropsConfigByIdQueryDto
            {
                Register = await _context.GlobalDropsConfig
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id)
            };
        }

        public async Task<GetHatchConfigByIdQueryDto> GetHatchConfigByIdAsync(long id)
        {
            return new GetHatchConfigByIdQueryDto
            {
                Register = await _context.HatchConfig
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id)
            };
        }

        public async Task<GetHatchConfigsQueryDto> GetHatchConfigsAsync(
            int limit,
            int offset,
            string sortColumn,
            SortDirectionEnum sortDirection,
            string? filter)
        {
            var result = new GetHatchConfigsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Asc;

            var query = _context.HatchConfig.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filter) && filter.Length >= 2)
            {
                if (int.TryParse(filter, out int numericFilter))
                {
                    var enumValue = (HatchTypeEnum)numericFilter;

                    query = query.Where(x =>
                        x.Type == enumValue ||
                        x.SuccessChance.ToString().Contains(filter) ||
                        x.BreakChance.ToString().Contains(filter));
                }
                else
                {
                    query = query.Where(x =>
                        x.Type.ToString().Contains(filter) ||
                        x.SuccessChance.ToString().Contains(filter) ||
                        x.BreakChance.ToString().Contains(filter));
                }
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetPlayersQueryDto> GetPlayersAsync(
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection,
    string? filter)
        {
            var result = new GetPlayersQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Asc;

            var query = _context.Character
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Id.ToString().Contains(filter) ||
                    x.Name.Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetPlayerByIdQueryDto> GetPlayerByIdAsync(long playerId)
        {
            var result = new GetPlayerByIdQueryDto();

            result.Register = await _context.Character
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.Digimons)
                    .ThenInclude(d => d.Digiclone) // já corrigido 👍
                .Include(x => x.Progress)
                .SingleOrDefaultAsync(x => x.Id == playerId);

            return result;
        }

        public async Task<GetPlayerInventoryQueryDto> GetPlayerInventoryAsync(long playerId)
        {
            var result = new GetPlayerInventoryQueryDto();

            result.Player = await _context.Character
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.ItemList)
                    .ThenInclude(y => y.Items)
                .SingleOrDefaultAsync(x => x.Id == playerId);

            return result;
        }

        public async Task<GetEventsQueryDto> GetEventsAsync(
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection,
    string? filter)
        {
            var result = new GetEventsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Asc;

            var query = _context.EventConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.EventMaps)
                    .ThenInclude(y => y.Map)
                .Include(x => x.EventMaps)
                    .ThenInclude(y => y.Mobs)
                        .ThenInclude(y => y.Location)
                .Include(x => x.EventMaps)
                    .ThenInclude(y => y.Mobs)
                        .ThenInclude(y => y.ExpReward)
                .Include(x => x.EventMaps)
                    .ThenInclude(y => y.Mobs)
                        .ThenInclude(y => y.DropReward)
                            .ThenInclude(z => z.Drops)
                .Include(x => x.EventMaps)
                    .ThenInclude(y => y.Mobs)
                        .ThenInclude(y => y.DropReward)
                            .ThenInclude(z => z.BitsDrop)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Name.Contains(filter) ||
                    x.Description.Contains(filter) ||
                    x.Id.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetEventConfigByIdQueryDto> GetEventConfigByIdAsync(long id)
        {
            return new GetEventConfigByIdQueryDto
            {
                Register = await _context.EventConfig
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id)
            };
        }

        public async Task<GetEventMapsQueryDto> GetEventMapsAsync(
    long eventId,
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection,
    string? filter)
        {
            var result = new GetEventMapsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Asc;

            var query = _context.EventMapsConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Mobs)
                .Include(x => x.Map)
                .Where(x => x.EventConfigId == eventId)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Map.Name.Contains(filter) ||
                    x.MapId.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetEventMapMobsQueryDto> GetEventMapMobsAsync(
            long mapId,
            int limit,
            int offset,
            string sortColumn,
            SortDirectionEnum sortDirection,
            string? filter)
        {
            var result = new GetEventMapMobsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Desc;

            var query = _context.EventMobConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.Drops)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.BitsDrop)
                .Where(x => x.EventMapConfigId == mapId)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Name.Contains(filter) ||
                    x.Type.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetEventMapMobByIdQueryDto> GetEventMapMobByIdAsync(long id)
        {
            var result = new GetEventMapMobByIdQueryDto();

            result.Register = await _context.EventMobConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.Drops)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.BitsDrop)
                .SingleOrDefaultAsync(x => x.Id == id);

            return result;
        }

        public async Task<GetEventMapRaidsQueryDto> GetEventMapRaidsAsync(
            long mapId,
            int limit,
            int offset,
            string sortColumn,
            SortDirectionEnum sortDirection,
            string? filter)
        {
            var result = new GetEventMapRaidsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Desc;

            var query = _context.EventMobConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.Drops)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.BitsDrop)
                .Where(x => x.EventMapConfigId == mapId && x.Class == 8) // sempre raids
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Name.Contains(filter) ||
                    x.Type.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }

        public async Task<GetEventMapByIdQueryDto> GetEventMapByIdAsync(long id)
        {
            return new GetEventMapByIdQueryDto
            {
                Register = await _context.EventMapsConfig
                    .AsNoTracking()
                    .Include(x => x.Map)
                    .SingleOrDefaultAsync(x => x.Id == id)
            };
        }

        public async Task<GetMapConfigQueryDto> GetMapConfigAsync(string filter)
        {
            var result = new GetMapConfigQueryDto();

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 2)
                filter = string.Empty;

            var query = _context.MapConfig
                .AsNoTracking()
                .Where(x => x.Type == MapTypeEnum.Event);

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.MapId.ToString().Contains(filter) ||
                    x.Name.Contains(filter));
            }

            result.Registers = await query.ToListAsync();

            return result;
        }

        public async Task<GetEventMobByIdQueryDto> GetEventMobByIdAsync(long id)
        {
            return new GetEventMobByIdQueryDto
            {
                Register = await _context.EventMobConfig
                    .AsNoTracking()
                    .AsSplitQuery()
                    .Include(x => x.Location)
                    .Include(x => x.ExpReward)
                    .Include(x => x.DropReward)
                        .ThenInclude(y => y.Drops)
                    .Include(x => x.DropReward)
                        .ThenInclude(y => y.BitsDrop)
                    .SingleOrDefaultAsync(x => x.Id == id)
            };
        }

        public async Task<GetEventRaidsQueryDto> GetEventRaidsAsync(
    long mapId,
    int limit,
    int offset,
    string sortColumn,
    SortDirectionEnum sortDirection,
    string? filter)
        {
            var result = new GetEventRaidsQueryDto();

            if (string.IsNullOrEmpty(sortColumn))
                sortColumn = "Id";

            if (string.IsNullOrWhiteSpace(filter) || filter.Length < 3)
                filter = string.Empty;

            if (sortDirection == SortDirectionEnum.None)
                sortDirection = SortDirectionEnum.Desc;

            var query = _context.EventMobConfig
                .AsNoTracking()
                .AsSplitQuery()
                .Include(x => x.Location)
                .Include(x => x.ExpReward)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.Drops)
                .Include(x => x.DropReward)
                    .ThenInclude(y => y.BitsDrop)
                .Where(x => x.EventMapConfigId == mapId && x.Class == 8);

            if (!string.IsNullOrEmpty(filter))
            {
                query = query.Where(x =>
                    x.Name.Contains(filter) ||
                    x.Type.ToString().Contains(filter));
            }

            result.TotalRegisters = await query.CountAsync();

            result.Registers = await query
                .OrderBy($"{sortColumn} {(sortDirection == SortDirectionEnum.Asc ? "ascending" : "descending")}")
                .Skip(offset)
                .Take(limit)
                .ToListAsync();

            return result;
        }
    }
}