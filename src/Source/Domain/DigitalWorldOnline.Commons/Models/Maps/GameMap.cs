using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Config.Events;
using DigitalWorldOnline.Commons.Models.Map.Dungeons;
using DigitalWorldOnline.Commons.Models.TamerShop;

namespace DigitalWorldOnline.Commons.Models.Map
{
    public sealed partial class GameMap : MapConfigModel
    {
        // Dynamic
        public byte Channel { get; set; }
        public DateTime WithoutTamers { get; private set; }
        public DateTime NextDatabaseOperation { get; private set; }
        public bool Initialized { get; private set; }
        public bool Operating { get; private set; }
        public bool UpdateMobs { get; private set; }
        public List<MobConfigModel> MobsToAdd { get; private set; } = new();
        public List<MobConfigModel> MobsToRemove { get; private set; } = new();
        public List<EventMobConfigModel> EventMobsToAdd { get; private set; } = new();
        public List<EventMobConfigModel> EventMobsToRemove { get; private set; } = new();
        public List<GameClient> Clients { get; private set; } = new();
        public List<Drop> Drops { get; private set; } = new();
        public List<ConsignedShop> ConsignedShops { get; private set; } = new();
        public Dictionary<long, List<long>> TamersView { get; private set; } = new();
        public Dictionary<long, List<long>> MobsView { get; private set; } = new();
        public Dictionary<long, List<long>> DropsView { get; private set; } = new();
        public Dictionary<long, List<long>> ConsignedShopView { get; private set; } = new();
        public Dictionary<short, long> TamerHandlers { get; private set; } = new();
        public Dictionary<short, long> DigimonHandlers { get; private set; } = new();
        public Dictionary<short, long> MobHandlers { get; private set; } = new();
        public Dictionary<short, long> DropHandlers { get; private set; } = new();
        public List<int> ColiseumMobs { get; private set; } = new();

        public object DropsLock { get; } = new object();
        public object ClientsLock { get; } = new object();
        public object DigimonHandlersLock { get; } = new object();
        public object TamerHandlersLock { get; } = new object();
        public bool Hidden { get; private set; }

        public void SetHidden(bool hidden)
        {
            Hidden = hidden;

            if (hidden)
            {
                foreach (var tamerKey in TamersView.Keys)
                {
                    if (TamersView[tamerKey].Contains(Id))
                    {
                        TamersView[tamerKey].Remove(Id);
                    }
                }
            }
        }

        public bool IsRoyalBase { get; private set; }
        public RoyalBaseMap? RoyalBaseMap { get; private set; }

        public GameMap(short mapId, List<MobConfigModel> mobs, List<Drop> drops) : base(mapId, mobs)
        {
            Drops = drops ?? new List<Drop>();
        }

        public GameMap(int mapId, List<EventMobConfigModel> mobs, List<Drop> drops) : base(mapId, mobs)
        {
            Drops = drops ?? new List<Drop>();
        }

        public GameMap()
        {
            Channel = 0;
            WithoutTamers = DateTime.MaxValue;
            NextDatabaseOperation = DateTime.Now.AddSeconds(30);
            IsRoyalBase = false;
        }
    }
}