using AutoMapper;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Assets;
using DigitalWorldOnline.Commons.Models.Assets.XML.InfiniteWar;
using DigitalWorldOnline.Commons.Models.Assets.XML.MapObject;
using DigitalWorldOnline.Commons.Models.Assets.XML.Tactics;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Events;
using DigitalWorldOnline.Commons.Models.Summon;
using FluentValidation;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Application
{
    public class AssetsLoader
    {
        private readonly ISender _sender;
        private readonly IMapper _mapper;
        private readonly ILogger _logger;
        private bool? _loading;
        private readonly object _lock = new object();
        private DateTime _lastLoadTime = DateTime.MinValue;
        private readonly TimeSpan _reloadInterval = TimeSpan.FromMinutes(1);
        private readonly string _appBaseDirectory = AppContext.BaseDirectory;
        private readonly string _assetsFolderPath;
        private readonly string _mapObjectXmlPath;
        private readonly string _TacticsHatchXmlPath;
        private readonly string _ItemlistXmlPath;
        private readonly string _InfiniteWar_RankRewardItemsXmlPath;

        private void LogMessage(ConsoleColor color, string message)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;

            Console.WriteLine("|----------------------------------------------------|");
            Console.WriteLine("|                                                    |");
            Console.WriteLine("|               ██████   ████████  ██    ██          |");
            Console.WriteLine("|               ██   ██     ██     ██    ██          |");
            Console.WriteLine("|               ██   ██     ██     ██    ██          |");
            Console.WriteLine("|               ██   ██     ██     ██    ██          |");
            Console.WriteLine("|               ██████      ██      ██████           |");
            Console.WriteLine("|                                                    |");
            Console.WriteLine("|----------------------------------------------------|");

            PrintCenteredLine("Um novo desafio se aproxima...");
            PrintCenteredLine("As trevas emergem das profundezas digitais.");
            PrintCenteredLine("Somente os mais fortes sobreviverão.");
            PrintCenteredLine("A jornada começa agora.");

            Console.WriteLine("|                                                    |");
            Console.WriteLine("|----------------------------------------------------|");

            PrintCenteredLine(message.ToUpper());
            Console.WriteLine("|                                                    |");
            PrintCenteredLine("DTU");
            Console.WriteLine("|----------------------------------------------------|");

            Console.ResetColor();
        }

        private void PrintCenteredLine(string text)
        {
            int totalWidth = 52;
            int padding = (totalWidth - text.Length) / 2;
            string line = "|" + new string(' ', padding) + text + new string(' ', totalWidth - text.Length - padding) + "|";
            Console.WriteLine(line);
        }

        public bool Loading => _loading == null || _loading.Value;

        public List<ItemAssetModel> ItemInfo { get; private set; }
        public List<SummonModel> SummonInfo { get; private set; }
        public List<SummonMobModel> SummonMobInfo { get; private set; }
        public List<CharacterLevelStatusAssetModel> TamerLevelInfo { get; private set; }
        public List<CharacterBaseStatusAssetModel> TamerBaseInfo { get; private set; }
        public List<DigimonLevelStatusAssetModel> DigimonLevelInfo { get; private set; }
        public List<DigimonBaseInfoAssetModel> DigimonBaseInfo { get; private set; }
        public List<DigimonSkillAssetModel> DigimonSkillInfo { get; private set; }
        public List<MonsterSkillAssetModel> MonsterSkill { get; private set; }
        public List<SkillCodeAssetModel> SkillCodeInfo { get; private set; }
        public List<SkillInfoAssetModel> SkillInfo { get; private set; }
        public List<MonsterSkillInfoAssetModel> MonsterSkillInfo { get; private set; }
        public List<MonthlyEventAssetModel> MonthlyEvents { get; private set; }
        public List<AchievementAssetModel> AchievementAssets { get; private set; }
        public List<SealDetailAssetModel> SealInfo { get; private set; }
        public List<EvolutionAssetModel> EvolutionInfo { get; private set; }
        public List<BuffInfoAssetModel> BuffInfo { get; private set; }
        public List<ScanDetailAssetModel> ScanDetail { get; private set; }
        public List<ContainerAssetModel> Container { get; private set; }
        public List<StatusApplyAssetModel> StatusApply { get; private set; }
        public List<TitleStatusAssetModel> TitleStatus { get; private set; }
        public List<AccessoryRollAssetModel> AccessoryRoll { get; private set; }
        public List<PortalAssetModel> Portal { get; private set; }
        public List<HatchAssetModel> Hatchs { get; private set; }
        public List<QuestAssetModel> Quest { get; private set; }
        public List<int> QuestItemList { get; private set; }
        public List<short> DailyQuestList { get; private set; }
        public List<MapAssetModel> Maps { get; private set; }
        public List<CloneAssetModel> Clones { get; private set; }
        public List<CloneValueAssetModel> CloneValues { get; private set; }
        public List<TamerSkillAssetModel> TamerSkills { get; private set; }
        public List<NpcAssetModel> Npcs { get; private set; }
        public List<NpcColiseumAssetModel> NpcColiseum { get; private set; }
        public List<ArenaRankingDailyItemRewardsModel> ArenaRankingDailyItemRewards { get; private set; }
        public List<EvolutionArmorAssetModel> EvolutionsArmor { get; private set; }
        public List<ExtraEvolutionNpcAssetModel> ExtraEvolutions { get; private set; }
        public List<CashShopAssetModel> CashShopAssets { get; private set; }
        public List<TimeRewardAssetModel> TimeRewardAssets { get; private set; }
        public List<TimeRewardModel> TimeRewardEvents { get; private set; }
        public List<GotchaAssetModel> Gotcha { get; private set; }
        public List<DeckBuffModel> DeckBuffs { get; private set; }

        public List<MapObjectAssetModel> MapObjects { get; private set; }
        public List<TacticHatchAssetModel> HatchXML { get; private set; }
        public List<InfiniteWar_RankRewardItemsXmlModel> InfiniteWar_RankRewardItems { get; private set; }

        public AssetsLoader(ISender sender, IMapper mapper, ILogger logger)
        {
            _sender = sender;
            _mapper = mapper;
            _logger = logger;
            string folderPath = Path.GetFullPath(Path.Combine(_appBaseDirectory, "..\\..\\..\\..\\..\\..\\"));
            _assetsFolderPath = Path.Combine(folderPath, "Assets_xml");

            _mapObjectXmlPath = Path.Combine(_assetsFolderPath, "MapObject.xml");
            _TacticsHatchXmlPath = Path.Combine(_assetsFolderPath, "Tactics_Hatch.xml");
            _InfiniteWar_RankRewardItemsXmlPath = Path.Combine(_assetsFolderPath, "InfiniteWar_RankRewardItems.xml");
        }

        public AssetsLoader Load()
        {
            lock (_lock)
            {
                if (_loading == null)
                {
                    _loading = true;
                    _lastLoadTime = DateTime.Now;
                    Task.Run(LoadAssets).ContinueWith(t =>
                    {
                        lock (_lock)
                        {
                            _loading = false;
                        }
                    });
                }
            }
            return this;
        }

        public AssetsLoader Reload()
        {
            lock (_lock)
            {
                if (_loading == false)
                {
                    if (DateTime.Now - _lastLoadTime < _reloadInterval)
                    {
                        return this;
                    }

                    _loading = true;
                    _lastLoadTime = DateTime.Now;
                    Task.Run(LoadAssets).ContinueWith(t =>
                    {
                        lock (_lock)
                        {
                            _loading = false;
                        }
                    });
                }
            }
            return this;
        }

        private async Task LoadAssets()
        {
            lock (_lock)
            {
                if (_loading == null)
                    return;
            }

            try
            {
                _logger.Information("=== Starting Asset Loading ===");

                ItemInfo = _mapper.Map<List<ItemAssetModel>>(await _sender.Send(new ItemAssetsQuery()));
                _logger.Information("ItemAssets loaded successfully.");

                SummonInfo = _mapper.Map<List<SummonModel>>(await _sender.Send(new SummonAssetsQuery()));
                _logger.Information("SummonAssets loaded successfully.");

                SummonMobInfo = _mapper.Map<List<SummonMobModel>>(await _sender.Send(new SummonMobAssetsQuery()));
                _logger.Information("SummonMobAssets loaded successfully.");

                SkillCodeInfo = _mapper.Map<List<SkillCodeAssetModel>>(await _sender.Send(new SkillCodeAssetsQuery()));
                _logger.Information("SkillCodeAssets loaded successfully.");

                TamerLevelInfo = _mapper.Map<List<CharacterLevelStatusAssetModel>>(await _sender.Send(new TamerLevelingAssetsQuery()));
                _logger.Information("TamerLevelingAssets loaded successfully.");

                TamerBaseInfo = _mapper.Map<List<CharacterBaseStatusAssetModel>>(await _sender.Send(new TamerBaseStatusAssetsQuery()));
                _logger.Information("TamerBaseStatusAssets loaded successfully.");

                DigimonLevelInfo = _mapper.Map<List<DigimonLevelStatusAssetModel>>(await _sender.Send(new DigimonLevelingAssetsQuery()));
                _logger.Information("DigimonLevelingAssets loaded successfully.");

                DigimonBaseInfo = _mapper.Map<List<DigimonBaseInfoAssetModel>>(await _sender.Send(new AllDigimonBaseInfoQuery()));
                _logger.Information("AllDigimonBaseInfo loaded successfully.");

                SkillInfo = _mapper.Map<List<SkillInfoAssetModel>>(await _sender.Send(new SkillInfoAssetsQuery()));
                _logger.Information("SkillInfoAssets loaded successfully.");

                DigimonSkillInfo = _mapper.Map<List<DigimonSkillAssetModel>>(await _sender.Send(new DigimonSkillAssetsQuery()));
                _logger.Information("DigimonSkillAssets loaded successfully.");

                MonsterSkill = _mapper.Map<List<MonsterSkillAssetModel>>(await _sender.Send(new MonsterSkillAssetsQuery()));
                _logger.Information("MonsterSkillAssets loaded successfully.");

                MonsterSkillInfo = _mapper.Map<List<MonsterSkillInfoAssetModel>>(await _sender.Send(new MonsterSkillInfoAssetsQuery()));
                _logger.Information("MonsterSkillInfoAssets loaded successfully.");

                SealInfo = _mapper.Map<List<SealDetailAssetModel>>(await _sender.Send(new SealStatusAssetsQuery()));
                _logger.Information("SealStatusAssets loaded successfully.");

                EvolutionInfo = _mapper.Map<List<EvolutionAssetModel>>(await _sender.Send(new DigimonEvolutionAssetsQuery()));
                _logger.Information("DigimonEvolutionAssets loaded successfully.");

                BuffInfo = _mapper.Map<List<BuffInfoAssetModel>>(await _sender.Send(new BuffInfoAssetsQuery()));
                _logger.Information("BuffInfoAssets loaded successfully.");

                ScanDetail = _mapper.Map<List<ScanDetailAssetModel>>(await _sender.Send(new ScanDetailAssetQuery()));
                _logger.Information("ScanDetailAsset loaded successfully.");

                Container = _mapper.Map<List<ContainerAssetModel>>(await _sender.Send(new ContainerAssetQuery()));
                _logger.Information("ContainerAsset loaded successfully.");

                StatusApply = _mapper.Map<List<StatusApplyAssetModel>>(await _sender.Send(new StatusApplyAssetQuery()));
                _logger.Information("StatusApplyAsset loaded successfully.");

                TitleStatus = _mapper.Map<List<TitleStatusAssetModel>>(await _sender.Send(new AllTitleStatusAssetsQuery()));
                _logger.Information("TitleStatusAssets loaded successfully.");

                AccessoryRoll = _mapper.Map<List<AccessoryRollAssetModel>>(await _sender.Send(new AccessoryRollAssetsQuery()));
                _logger.Information("AccessoryRollAssets loaded successfully.");

                Portal = _mapper.Map<List<PortalAssetModel>>(await _sender.Send(new PortalAssetsQuery()));
                _logger.Information("PortalAssets loaded successfully.");

                Npcs = _mapper.Map<List<NpcAssetModel>>(await _sender.Send(new NpcAssetsQuery()));
                _logger.Information("NpcAssets loaded successfully.");

                NpcColiseum = _mapper.Map<List<NpcColiseumAssetModel>>(await _sender.Send(new NpcColiseumAssetsQuery()));
                _logger.Information("NpcColiseumAssets loaded successfully.");

                Quest = _mapper.Map<List<QuestAssetModel>>(await _sender.Send(new QuestAssetsQuery()));
                _logger.Information("QuestAssets loaded successfully.");

                Hatchs = _mapper.Map<List<HatchAssetModel>>(await _sender.Send(new HatchAssetsQuery()));
                _logger.Information("HatchAssets loaded successfully.");

                Maps = _mapper.Map<List<MapAssetModel>>(await _sender.Send(new MapAssetsQuery()));
                _logger.Information("MapAssets loaded successfully.");

                Clones = _mapper.Map<List<CloneAssetModel>>(await _sender.Send(new CloneAssetsQuery()));
                _logger.Information("CloneAssets loaded successfully.");

                CloneValues = _mapper.Map<List<CloneValueAssetModel>>(await _sender.Send(new CloneValueAssetsQuery()));
                _logger.Information("CloneValueAssets loaded successfully.");

                TamerSkills = _mapper.Map<List<TamerSkillAssetModel>>(await _sender.Send(new TamerSkillAssetsQuery()));
                _logger.Information("TamerSkillAssets loaded successfully.");

                MonthlyEvents = _mapper.Map<List<MonthlyEventAssetModel>>(await _sender.Send(new MonthlyEventAssetsQuery()));
                _logger.Information("MonthlyEventAssets loaded successfully.");

                AchievementAssets = _mapper.Map<List<AchievementAssetModel>>(await _sender.Send(new AchievementAssetsQuery()));
                _logger.Information("AchievementAssets loaded successfully.");

                EvolutionsArmor = _mapper.Map<List<EvolutionArmorAssetModel>>(await _sender.Send(new EvolutionArmorAssetsQuery()));
                _logger.Information("EvolutionArmorAssets loaded successfully.");

                ExtraEvolutions = _mapper.Map<List<ExtraEvolutionNpcAssetModel>>(await _sender.Send(new ExtraEvolutionNpcAssetQuery()));
                _logger.Information("ExtraEvolutionNpcAssets loaded successfully.");

                CashShopAssets = _mapper.Map<List<CashShopAssetModel>>(await _sender.Send(new CashShopAssetsQuery()));
                _logger.Information("CashShopAssets loaded successfully.");

                TimeRewardAssets = _mapper.Map<List<TimeRewardAssetModel>>(await _sender.Send(new TimeRewardAssetsQuery()));
                _logger.Information("TimeRewardAssets loaded successfully.");

                TimeRewardEvents = _mapper.Map<List<TimeRewardModel>>(await _sender.Send(new TimeRewardEventsQuery()));
                _logger.Information("TimeRewardEvents loaded successfully.");

                DeckBuffs = _mapper.Map<List<DeckBuffModel>>(await _sender.Send(new DeckBuffAssetsQuery()));
                _logger.Information("DeckBuffAssets loaded successfully.");

                Gotcha = _mapper.Map<List<GotchaAssetModel>>(await _sender.Send(new GotchaAssetsQuery()));
                _logger.Information("GotchaAssets loaded successfully.");
            }
            catch (Exception ex)
            {
                _logger.Error("❌ Error on Loading Assets (AssetsLoader.cs): {Message}\nStackTrace: {StackTrace}", ex.Message, ex.StackTrace);
            }

            try
            {
                var mapObjectAssetWrapper = MapObjectReader.LoadFromXml(_mapObjectXmlPath);
                if (mapObjectAssetWrapper != null && mapObjectAssetWrapper.MapObjects != null)
                    MapObjects = mapObjectAssetWrapper.MapObjects;
                else
                {
                    MapObjects = new List<MapObjectAssetModel>();
                    _logger.Error($"Can't load MapObject XML: {_mapObjectXmlPath}");
                }

                var tacticHatchAssetWrapper = TacticHatchReader.LoadFromXml(_TacticsHatchXmlPath);
                if (tacticHatchAssetWrapper != null && tacticHatchAssetWrapper.Hatch != null)
                    HatchXML = tacticHatchAssetWrapper.Hatch;
                else
                {
                    HatchXML = new List<TacticHatchAssetModel>();
                    _logger.Error($"Can't load Tactics_Hatch XML: {_TacticsHatchXmlPath}");
                }

                var infiniteWar_RankRewardItemsXmlWrapper = InfiniteWar_RankRewardItemsReader.LoadFromXml(_InfiniteWar_RankRewardItemsXmlPath);
                if (infiniteWar_RankRewardItemsXmlWrapper != null && infiniteWar_RankRewardItemsXmlWrapper.InfiniteWar_RankRewardItems != null)
                    InfiniteWar_RankRewardItems = infiniteWar_RankRewardItemsXmlWrapper.InfiniteWar_RankRewardItems;
                else
                {
                    InfiniteWar_RankRewardItems = new List<InfiniteWar_RankRewardItemsXmlModel>();
                    _logger.Error($"Can't load InfiniteWar_RankRewardItems.xml: {_InfiniteWar_RankRewardItemsXmlPath}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error on Loading xml Assets:\n {Message}\nStackTrace: {StackTrace}", ex.Message, ex.StackTrace);
            }

            LogMessage(ConsoleColor.Yellow, "ASSETS LOADED !!");

            try
            {
                ItemInfo.ForEach(item => item.SetSkillInfo(SkillCodeInfo.FirstOrDefault(x => x.SkillCode == item.SkillCode)));
                BuffInfo.ForEach(buff => buff.SetSkillInfo(SkillCodeInfo.FirstOrDefault(x => x.SkillCode == buff.SkillCode || x.SkillCode == buff.DigimonSkillCode)));
                DigimonSkillInfo.ForEach(skill => skill.SetSkillInfo(SkillInfo.FirstOrDefault(x => x.SkillId == skill.SkillId)));
                MonsterSkill.ForEach(skill => skill.SetSkillInfo(MonsterSkillInfo.FirstOrDefault(x => x.SkillId == skill.SkillId)));
                SealInfo = SealInfo.OrderByDescending(x => x.RequiredAmount).ToList();
                QuestItemList = ItemInfo.Where(x => x.Type == 80 || x.Type == 85).Select(x => x.ItemId).ToList();
                DailyQuestList = Quest.Where(x => x.QuestType == QuestTypeEnum.DailyQuest).Select(x => (short)x.QuestId).ToList();
            }
            catch (Exception ex)
            {
                _logger.Error("Error on Loading additional Assets (AssetsLoader.cs): {Message}\nStackTrace: {StackTrace}", ex.Message, ex.StackTrace);
            }
        }
    }
}
