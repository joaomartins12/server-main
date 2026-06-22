using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.Map;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.ViewModel.Summons;

namespace DigitalWorldOnline.Commons.Models.Summon
{
    public sealed partial class SummonMobModel :StatusAssetModel, ICloneable, IMob
    {
        /// <summary>
        /// Sequencial unique identifier.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Client reference for digimon type.
        /// </summary>
        public int Type { get; set; }

        /// <summary>
        /// Monster spawn duration.
        /// </summary>
        public int Duration { get; private set; }

        /// <summary>
        /// Monster spawn amount.
        /// </summary>
        public int Amount { get; set; }

        /// <summary>
        /// Client reference for digimon model.
        /// </summary>
        public int Model { get; set; }

        /// <summary>
        /// Digimon name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Base digimon level.
        /// </summary>
        public byte Level { get; set; }

        /// <summary>
        /// View range (from current position) for aggressive mobs.
        /// </summary>
        public int ViewRange { get; set; }

        /// <summary>
        /// Hunt range (from start position) for giveup on chasing targets.
        /// </summary>
        public int HuntRange { get; set; }


        /// <summary>
        /// Monster class type enumeration. 8 = Raid Boss
        /// </summary>
        public int Class { get; set; }


        /// <summary>
        /// Mob reaction type.
        /// </summary>
        public DigimonReactionTypeEnum ReactionType { get; set; }

        /// <summary>
        /// Mob attribute.
        /// </summary>
        public DigimonAttributeEnum Attribute { get; set; }

        /// <summary>
        /// Mob element.
        /// </summary>
        public DigimonElementEnum Element { get; set; }

        /// <summary>
        /// Mob main family.
        /// </summary>
        public DigimonFamilyEnum Family1 { get; set; }

        /// <summary>
        /// Mob second family.
        /// </summary>
        public DigimonFamilyEnum Family2 { get; set; }

        /// <summary>
        /// Mob third family.
        /// </summary>
        public DigimonFamilyEnum Family3 { get; set; }

        /// <summary>
        /// Total Attack Speed value.
        /// </summary>
        public int ASValue { get; private set; }

        /// <summary>
        /// Total Attack Range value.
        /// </summary>
        public int ARValue { get; private set; }

        /// <summary>
        /// Total Attack value.
        /// </summary>
        public int ATValue { get; private set; }

        /// <summary>
        /// Total Block value.
        /// </summary>
        public int BLValue { get; private set; }

        /// <summary>
        /// Total Critical value.
        /// </summary>
        public int CTValue { get; private set; } //TODO: separar CR e CD

        /// <summary>
        /// Total Defense value.
        /// </summary>
        public int DEValue { get; private set; }

        /// <summary>
        /// Total DigiSoul value.
        /// </summary>
        public int DSValue { get; private set; }

        /// <summary>
        /// Total Evasion value.
        /// </summary>
        public int EVValue { get; private set; }

        /// <summary>
        /// Total Health value.
        /// </summary>
        public int HPValue { get; set; }

        /// <summary>
        /// Total Hit Rate value.
        /// </summary>
        public int HTValue { get; private set; }

        /// <summary>
        /// Total Run Speed value.
        /// </summary>
        public int MSValue { get; set; }

        /// <summary>
        /// Total Walk Speed value.
        /// </summary>
        public int WSValue { get; private set; }

        /// <summary>
        /// Drop config.
        /// </summary>
        public SummonMobDropRewardModel? DropReward { get; private set; }

        /// <summary>
        /// Exp config.
        /// </summary>
        public SummonMobExpRewardModel? ExpReward { get; private set; }

        public SummonMobLocationModel Location { get; set; }
        public MobDebuffListModel DebuffList { get; set; }

        //Dynamic
        public MobActionEnum CurrentAction { get; set; }
        public DateTime LastActionTime { get; set; }
        public DateTime ExpirationDate { get; private set; }
        public DateTime StartDate { get; private set; }
        public DateTime LastSkillTryTime { get; set; }
        public DateTime NextWalkTime { get; private set; }
        public DateTime AgressiveCheckTime { get; private set; }
        public DateTime ViewCheckTime { get; set; }
        public DateTime LastHitTime { get; set; }
        public DateTime LastSkillTime { get; set; }
        public DateTime LastHealTime { get; set; }
        public DateTime ChaseEndTime { get; set; }
        public DateTime BattleStartTime { get; set; } //TODO: utilizar no grow
        public DateTime LastHitTryTime { get; set; }
        public DateTime LasSkillTryTime { get; set; }
        public DateTime DieTime { get; set; }
        public int GeneralHandler { get; set; }
        public int TargetSummonHandler;
        public int CurrentHP { get; set; }
        public int Cooldown { get; set; }
        public Location CurrentLocation { get; set; }
        public Location PreviousLocation { get; set; }
        public Location InitialLocation { get; set; }
        public byte MoveCount { get; set; }
        public byte GrowStack { get; set; }
        public byte DisposedObjects { get; set; }
        public bool InBattle { get; set; }
        public bool AwaitingKillSpawn { get; set; }
        public bool Dead { get; set; }
        public bool Respawn { get; set; }
        public bool CheckSkill { get; set; }
        public bool IsPossibleSkill => ((double)CurrentHP / HPValue) * 100 <= 90;
        public bool BossMonster { get; set; } //TODO: ajustar valor conforme type
        public List<CharacterModel> TargetTamers { get; set; }
        public Dictionary<long,int> RaidDamage { get; set; }
        public List<long> TamersViewing { get; set; }

        public void initialLocation()
        {
            CurrentLocation = Location;
            PreviousLocation = Location;
            InitialLocation = Location;
        }
        public SummonMobModel()
        {
            TamersViewing = new List<long>();
            RaidDamage = new Dictionary<long,int>();
            TargetTamers = new List<CharacterModel>();
            Location = new SummonMobLocationModel();
            DebuffList = new MobDebuffListModel();
            Duration = 0;

            CurrentAction = MobActionEnum.Wait;
            LastActionTime = DateTime.Now;
            AgressiveCheckTime = DateTime.Now;
            ViewCheckTime = DateTime.Now;
            NextWalkTime = DateTime.Now;
            ChaseEndTime = DateTime.Now;
            LastHealTime = DateTime.Now;
            LastHitTime = DateTime.Now;
            LastHitTryTime = DateTime.Now;
            LastSkillTryTime = DateTime.Now;
            DieTime = DateTime.Now;
        }

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}
