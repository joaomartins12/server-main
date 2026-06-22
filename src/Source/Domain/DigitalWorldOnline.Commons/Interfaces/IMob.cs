using System;
using System.Collections.Generic;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.Map;
using DigitalWorldOnline.Commons.Models;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Summon;

namespace DigitalWorldOnline.Commons.Interfaces
{
    public interface IMob
    {
        // General Properties
        long Id { get; set; }
        int Type { get; set; }
        int Model { get; set; }
        string Name { get; set; }
        byte Level { get; set; }
        int ViewRange { get; set; }
        int HuntRange { get; set; }
        int Class { get; set; }

        // Mob Reaction and Attribute
        DigimonReactionTypeEnum ReactionType { get; set; }
        DigimonAttributeEnum Attribute { get; set; }
        DigimonElementEnum Element { get; set; }
        DigimonFamilyEnum Family1 { get; set; }
        DigimonFamilyEnum Family2 { get; set; }
        DigimonFamilyEnum Family3 { get; set; }

        // Combat Stats
        int HPValue { get; set; }
        int CurrentHP { get; set; }
        int Cooldown { get; set; }
        int ATValue { get; set; }
        int EVValue { get; set; }
        int DEValue { get; set; }
        int BLValue { get; set; }

        public int ASValue { get; set; }
        byte CurrentHpRate { get; }

        public bool Alive => CurrentHP > 1;


        // Location Information
        Location CurrentLocation { get; set; }
        Location PreviousLocation { get; set; }
        Location InitialLocation { get; set; }
        List<long> TamersViewing { get; set; }
        List<CharacterModel> TargetTamers { get; set; }
        Dictionary<long,int> RaidDamage { get; set; }

        // Mob Status
        bool InBattle { get; set; }
        bool Dead { get; set; }
        bool Respawn { get; set; }
        bool CheckSkill { get; set; }
        bool IsPossibleSkill { get; }
        bool BossMonster { get; set; }
        bool AwaitingKillSpawn { get; set; }

        // Action Handling

        public MobActionEnum CurrentAction { get; set; }
        DateTime LastActionTime { get; set; }
        DateTime LastSkillTryTime { get; set; }
        DateTime LastHitTime { get; set; }
        DateTime LastHealTime { get; set; }
        DateTime DieTime { get; set; }

        // Dynamic Properties
        DateTime ViewCheckTime { get; set; }
        DateTime ChaseEndTime { get; set; }
        DateTime BattleStartTime { get; set; }
        int GeneralHandler { get; set; }
        byte MoveCount { get; set; }
        byte GrowStack { get; set; }
        byte DisposedObjects { get; set; }

        // Methods
        void StartBattle(CharacterModel tamer);
        public void AddTarget(CharacterModel tamer);
        public bool CanMissHit();
        public void SetNextAction();
        void UpdateCurrentHp(int newValue); // Declare it in the interface

        int ReceiveDamage(int damage,long tamerId);
        void Die();
        public void UpdateCurrentAction(MobActionEnum action) => CurrentAction = action;

        //Models
        public MobDebuffListModel DebuffList { get; set; }



    }
}


