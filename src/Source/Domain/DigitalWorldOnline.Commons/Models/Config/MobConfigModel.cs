using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.Map;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Summon;

namespace DigitalWorldOnline.Commons.Models.Config
{
    public sealed partial class MobConfigModel :StatusAssetModel, ICloneable, IMob
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
        /// Digimon scaling type.
        /// </summary>
        public byte ScaleType { get; private set; }

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
        /// Monster coliseum Type
        /// </summary>
        public bool Coliseum { get; private set; }

        /// <summary>
        /// Monster coliseum Round
        /// </summary>
        public byte Round { get; private set; }

        public DungeonDayOfWeekEnum WeekDay { get; private set; }

        /// <summary>
        /// Mob Coliseum  type.
        /// </summary>
        public ColiseumMobTypeEnum ColiseumMobType { get; private set; }

        /// <summary>
        /// Digimon reaction type.
        /// </summary>
        public DigimonReactionTypeEnum ReactionType { get; set; }

        /// <summary>
        /// Digimon attribute.
        /// </summary>
        public DigimonAttributeEnum Attribute { get; set; }

        /// <summary>
        /// Digimon element.
        /// </summary>
        public DigimonElementEnum Element { get; set; }

        /// <summary>
        /// Digimon main family.
        /// </summary>
        public DigimonFamilyEnum Family1 { get; set; }

        /// <summary>
        /// Digimon second family.
        /// </summary>
        public DigimonFamilyEnum Family2 { get; set; }

        /// <summary>
        /// Digimon third family.
        /// </summary>
        public DigimonFamilyEnum Family3 { get; set; }

        /// <summary>
        /// Respawn interval in seconds.
        /// </summary>
        public int RespawnInterval { get; private set; }

        /// <summary>
        /// Initial location.
        /// </summary>
        public MobLocationConfigModel Location { get; set; }

        /// <summary>
        /// Drop config.
        /// </summary>
        public MobDropRewardConfigModel? DropReward { get; private set; }

        /// <summary>
        /// Exp config.
        /// </summary>
        public MobExpRewardConfigModel? ExpReward { get; private set; }

        public MobDebuffListModel DebuffList { get; set; }
        //Dynamic
        public MobActionEnum CurrentAction { get; set; }
        public DateTime LastActionTime { get; set; }
        public DateTime LastSkillTryTime { get; set; }
        public DateTime NextWalkTime { get; set; }
        public DateTime AgressiveCheckTime { get; set; }
        public DateTime ViewCheckTime { get; set; }
        public DateTime LastHitTime { get; set; }
        public DateTime LastSkillTime { get; set; }
        public DateTime LastHealTime { get; set; }
        public DateTime ChaseEndTime { get; set; }
        public DateTime BattleStartTime { get; set; } //TODO: utilizar no grow
        public DateTime LastHitTryTime { get; set; }
        public DateTime LasSkillTryTime { get; set; }
        public DateTime DieTime { get; set; }
        public DateTime? DeathTime { get; set; }
        public DateTime? ResurrectionTime { get; set; }
        public int GeneralHandler { get; set; }
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
        public bool Bless => Type == 45161 && Location.MapId == 1307;
        public bool Bless2 => Type == 62169 && (Location.MapId == 1300 || Location.MapId == 3 || Location.MapId == 3);

        public bool BossMonster { get; set; } //TODO: ajustar valor conforme type
        public List<CharacterModel> TargetTamers { get; set; }
        public Dictionary<long,int> RaidDamage { get; set; }
        public List<long> TamersViewing { get; set; }

        public MobConfigModel()
        {
            TamersViewing = new List<long>();
            RaidDamage = new Dictionary<long,int>();
            TargetTamers = new List<CharacterModel>();
            Location = new MobLocationConfigModel();
            RespawnInterval = 8;
            DebuffList = new MobDebuffListModel();
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
