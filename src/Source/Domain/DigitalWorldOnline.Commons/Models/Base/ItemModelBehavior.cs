using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Utils;
using System.Text;

namespace DigitalWorldOnline.Commons.Models.Base
{
    public partial class ItemModel : ICloneable
    {
        /// <summary>
        /// Sets the current power.
        /// </summary>
        public void SetPower(byte power) => Power = power;

        /// <summary>
        /// Sets the left reroll amount.
        /// </summary>
        public void SetReroll(byte reroll) => RerollLeft = reroll;

        public void SetFamilyType(byte familyType) => FamilyType = familyType;

        public ItemModel(int itemId, int amount)
        {
            ItemId = itemId;
            Amount = amount;

            if (Id == Guid.Empty)
                Id = Guid.NewGuid();

            EnsureStatusLists();
        }

        public uint RemainingSeconds()
        {
            if (ItemInfo == null || !ItemInfo.TemporaryItem)
                return 0;

            var time = (EndDate - DateTime.Now).TotalSeconds;

            if (time <= 0)
                return 0;

            return (uint)time;
        }

        public uint RemainingMinutes()
        {
            if (ItemInfo == null || !ItemInfo.TemporaryItem)
                return 0;

            if ((EndDate - DateTime.Now).TotalMinutes <= 0)
                return 0xFFFFFFFF;

            var time = (EndDate - DateTime.Now).TotalMinutes > 0
                ? (int)(EndDate - DateTime.Now).TotalMinutes
                : 0;

            return (uint)time;
        }

        public uint RemainingDays()
        {
            if (ItemInfo == null || !ItemInfo.TemporaryItem)
                return 0;

            var time = (EndDate - DateTime.Now).TotalDays;

            if (time <= 0)
                return 0;

            return (uint)time;
        }

        /// <summary>
        /// Flags the current item if it has been expired.
        /// </summary>
        public bool Expired
        {
            get
            {
                return ItemInfo != null &&
                       ItemInfo.UseTimeType > 0 &&
                       RemainingMinutes() == 0xFFFFFFFF &&
                       FirstExpired;
            }
        }

        /// <summary>
        /// Flag for accessory status.
        /// </summary>
        public bool HasAccessoryStatus => AccessoryStatus != null && AccessoryStatus.Any(x => x.Value > 0);

        /// <summary>
        /// Flag for socket status.
        /// </summary>
        public bool HasSocketStatus => SocketStatus != null && SocketStatus.Any(x => x.Value > 0);

        /// <summary>
        /// Returns the flag with the information about item duration.
        /// </summary>
        public bool IsTemporary => ItemInfo?.UseTimeType > 0;

        /// <summary>
        /// Sets the current remaining time of the target item to the base value, if possible.
        /// </summary>
        public bool SetDefaultRemainingTime()
        {
            if (ItemInfo != null && IsTemporary)
            {
                Duration = ItemInfo.UsageTimeMinutes;
                EndDate = DateTime.Now.AddMinutes(ItemInfo.UsageTimeMinutes);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Updates the current item id.
        /// </summary>
        public void SetItemId(int itemId = 0) => ItemId = itemId;

        /// <summary>
        /// Updates the remaining time.
        /// </summary>
        public void SetRemainingTime(uint remainingTime = 0)
        {
            if (remainingTime == 4294967280)
                remainingTime = 0;

            Duration = (int)remainingTime;
            EndDate = DateTime.Now.AddMinutes(remainingTime);
        }

        /// <summary>
        /// Updates the current amount.
        /// </summary>
        public void SetAmount(int amount = 0) => Amount = amount;

        public void SetSlot(int slot) => Slot = slot;

        public void SetTradeSlot(int slot) => TradeSlot = slot;

        /// <summary>
        /// Reduces the current amount.
        /// </summary>
        public void ReduceAmount(int amount) => Amount -= amount;

        /// <summary>
        /// Updates the sell price at tamer shop.
        /// </summary>
        public void SetSellPrice(long sellprice) => TamerShopSellPrice = sellprice;

        /// <summary>
        /// Updates the extra information about the item.
        /// </summary>
        public void SetItemInfo(ItemAssetModel? info) => ItemInfo = info;

        public void SetFirstExpired(bool firstExpired) => FirstExpired = firstExpired;

        /// <summary>
        /// Increases the current amount.
        /// </summary>
        public void IncreaseAmount(int amount) => Amount += amount;

        /// <summary>
        /// Serializes the current item into byte array.
        /// Always returns exactly 68 bytes.
        /// </summary>
        public byte[] ToArray(bool simplified = false)
        {
            EnsureStatusLists();

            using MemoryStream m = new();

            if (ItemId <= 0 || Amount <= 0)
            {
                WriteEmptyItem(m);
                return m.ToArray();
            }

            /*
                Safety:
                If ItemInfo is null, the server does not know this item in loaded assets.
                Sending an item like this can corrupt/desync the client packet.
                So we serialize it as an empty slot instead of throwing or sending invalid data.
            */
            if (ItemInfo == null)
            {
                Console.WriteLine(
                    $"[ItemModel.ToArray] ItemInfo NULL. Serializing empty slot. ItemListId={ItemListId} Slot={Slot} ItemId={ItemId} Amount={Amount}");

                WriteEmptyItem(m);
                return m.ToArray();
            }

            m.Write(BitConverter.GetBytes(ItemId), 0, 4);
            m.Write(BitConverter.GetBytes(Amount), 0, 4);

            if (simplified)
            {
                m.Write(new byte[60], 0, 60);
            }
            else
            {
                m.Write(BitConverter.GetBytes((short)0), 0, 2);
                m.Write(BitConverter.GetBytes((short)0), 0, 2);
                m.WriteByte(Power);
                m.WriteByte(RerollLeft);
                m.Write(BitConverter.GetBytes(ItemInfo.BoundType), 0, 2);

                foreach (var socketStatus in SocketStatus.OrderBy(x => x.Slot))
                    m.Write(BitConverter.GetBytes(socketStatus.AttributeId), 0, 2);

                foreach (var socketStatus in SocketStatus.OrderBy(x => x.Slot))
                    m.WriteByte((byte)Math.Clamp(socketStatus.Value, (short)0, (short)255));

                m.WriteByte(0);

                foreach (var accessoryStatus in AccessoryStatus.OrderBy(x => x.Slot))
                    m.Write(BitConverter.GetBytes(accessoryStatus.Type.GetHashCode()), 0, 2);

                foreach (var accessoryStatus in AccessoryStatus.OrderBy(x => x.Slot))
                    m.Write(BitConverter.GetBytes(accessoryStatus.Value), 0, 2);

                m.Write(BitConverter.GetBytes((short)0), 0, 2);

                var remainingMinutes = RemainingMinutes();

                if (remainingMinutes == 0 || remainingMinutes == 0xFFFFFFFF)
                {
                    m.Write(BitConverter.GetBytes(0), 0, 4);
                }
                else
                {
                    var ts = UtilitiesFunctions.RemainingTimeMinutes((int)remainingMinutes);
                    m.Write(BitConverter.GetBytes(ts), 0, 4);
                }

                m.Write(BitConverter.GetBytes(0), 0, 4);
            }

            return NormalizeItemPacketSize(m.ToArray());
        }

        /// <summary>
        /// Serializes the current gift item into byte array.
        /// </summary>
        public byte[] GiftToArray(bool simplified = false)
        {
            EnsureStatusLists();

            if (ItemId <= 0 || Amount <= 0)
                return Array.Empty<byte>();

            using MemoryStream m = new();

            m.Write(BitConverter.GetBytes(ItemId), 0, 4);
            m.Write(BitConverter.GetBytes(Amount), 0, 4);

            if (simplified)
            {
                m.Write(new byte[60], 0, 60);
            }
            else
            {
                m.Write(BitConverter.GetBytes((short)0), 0, 2);
                m.Write(BitConverter.GetBytes((short)0), 0, 2);
                m.WriteByte(Power);
                m.WriteByte(RerollLeft);

                var boundType = ItemInfo?.BoundType ?? 0;
                m.Write(BitConverter.GetBytes(boundType), 0, 2);

                m.Write(BitConverter.GetBytes((short)0), 0, 2);
                m.Write(BitConverter.GetBytes((short)0), 0, 2);
                m.Write(BitConverter.GetBytes((short)0), 0, 2);

                m.WriteByte(0);
                m.WriteByte(0);
                m.WriteByte(0);
                m.WriteByte(0);

                var orderedAccessoryStatusList = AccessoryStatus.OrderBy(x => x.Slot).ToList();

                foreach (var accessoryStatus in orderedAccessoryStatusList)
                    m.Write(BitConverter.GetBytes(accessoryStatus.Type.GetHashCode()), 0, 2);

                foreach (var accessoryStatus in orderedAccessoryStatusList)
                    m.Write(BitConverter.GetBytes(accessoryStatus.Value), 0, 2);

                m.Write(BitConverter.GetBytes((short)0), 0, 2);

                var remainingMinutes = RemainingMinutes();

                if (remainingMinutes == 4294967280 || remainingMinutes == 0xFFFFFFFF)
                {
                    m.Write(BitConverter.GetBytes(remainingMinutes), 0, 4);
                }
                else
                {
                    m.Write(BitConverter.GetBytes(UtilitiesFunctions.RemainingTimeMinutes((int)remainingMinutes)), 0, 4);
                }

                m.Write(BitConverter.GetBytes(0), 0, 4);
            }

            return NormalizeItemPacketSize(m.ToArray());
        }

        public byte[] NewGiftToArray()
        {
            EnsureStatusLists();

            if (ItemId <= 0 || Amount <= 0)
                return Array.Empty<byte>();

            using MemoryStream m = new();

            m.Write(BitConverter.GetBytes(ItemId), 0, 4);
            m.Write(BitConverter.GetBytes(Amount), 0, 4);
            m.Write(BitConverter.GetBytes((short)0), 0, 2);
            m.Write(BitConverter.GetBytes((short)0), 0, 2);
            m.WriteByte(Power);
            m.WriteByte(RerollLeft);

            var boundType = ItemInfo?.BoundType ?? 0;
            m.Write(BitConverter.GetBytes(boundType), 0, 2);

            m.Write(BitConverter.GetBytes((short)0), 0, 2);
            m.Write(BitConverter.GetBytes((short)0), 0, 2);
            m.Write(BitConverter.GetBytes((short)0), 0, 2);

            m.WriteByte(0);
            m.WriteByte(0);
            m.WriteByte(0);
            m.WriteByte(0);

            var orderedAccessoryStatusList = AccessoryStatus.OrderBy(x => x.Slot).ToList();

            foreach (var accessoryStatus in orderedAccessoryStatusList)
                m.Write(BitConverter.GetBytes(accessoryStatus.Type.GetHashCode()), 0, 2);

            foreach (var accessoryStatus in orderedAccessoryStatusList)
                m.Write(BitConverter.GetBytes(accessoryStatus.Value), 0, 2);

            m.Write(BitConverter.GetBytes((short)0), 0, 2);

            var unixEndTime = (uint)new DateTimeOffset(EndDate).ToUnixTimeSeconds();
            m.Write(BitConverter.GetBytes(unixEndTime), 0, 4);

            m.Write(BitConverter.GetBytes(0), 0, 4);

            return NormalizeItemPacketSize(m.ToArray());
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (ItemId > 0)
            {
                sb.AppendLine($"Amount {Amount}");
                sb.AppendLine($"Power {Power}");
                sb.AppendLine($"RerollLeft {RerollLeft}");
                sb.AppendLine($"BoundType {ItemInfo?.BoundType}");

                EnsureStatusLists();

                foreach (var accessoryStatus in AccessoryStatus.OrderBy(x => x.Slot))
                {
                    sb.AppendLine($"AccessoryStatus{accessoryStatus.Slot}");
                    sb.AppendLine($"Type {accessoryStatus.Type}");
                    sb.AppendLine($"Value {accessoryStatus.Value}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns the current amount of the target status type.
        /// </summary>
        public byte StatusAmount(AccessoryStatusTypeEnum type)
        {
            EnsureStatusLists();
            return (byte)AccessoryStatus.Count(x => x.Type == type);
        }

        /// <summary>
        /// Clones an item properties, but keeps the same identifier.
        /// </summary>
        public object Clone(Guid id)
        {
            var clonedObject = (ItemModel)Clone();
            clonedObject.Id = id;
            return clonedObject;
        }

        /// <summary>
        /// Clones an item properties.
        /// </summary>
        public object Clone()
        {
            var clone = (ItemModel)MemberwiseClone();

            clone.AccessoryStatus = AccessoryStatus?
                .Select(x => new ItemAccessoryStatusModel(x.Slot)
                {
                    Id = x.Id,
                    Type = x.Type,
                    Value = x.Value,
                    ItemId = x.ItemId
                })
                .ToList() ?? new List<ItemAccessoryStatusModel>();

            clone.SocketStatus = SocketStatus?
                .Select(x => new ItemSocketStatusModel(x.Slot)
                {
                    Id = x.Id,
                    AttributeId = x.AttributeId,
                    Type = x.Type,
                    Value = x.Value,
                    ItemId = x.ItemId
                })
                .ToList() ?? new List<ItemSocketStatusModel>();

            return clone;
        }

        private void EnsureStatusLists()
        {
            if (AccessoryStatus == null || AccessoryStatus.Count != 8)
            {
                AccessoryStatus = new List<ItemAccessoryStatusModel>
                {
                    new ItemAccessoryStatusModel(0),
                    new ItemAccessoryStatusModel(1),
                    new ItemAccessoryStatusModel(2),
                    new ItemAccessoryStatusModel(3),
                    new ItemAccessoryStatusModel(4),
                    new ItemAccessoryStatusModel(5),
                    new ItemAccessoryStatusModel(6),
                    new ItemAccessoryStatusModel(7)
                };
            }

            if (SocketStatus == null || SocketStatus.Count != 3)
            {
                SocketStatus = new List<ItemSocketStatusModel>
                {
                    new ItemSocketStatusModel(0),
                    new ItemSocketStatusModel(1),
                    new ItemSocketStatusModel(2)
                };
            }
        }

        private static void WriteEmptyItem(MemoryStream m)
        {
            for (int i = 0; i < GeneralSizeEnum.ItemSizeInBytes.GetHashCode(); i++)
                m.WriteByte(0);
        }

        private static byte[] NormalizeItemPacketSize(byte[] data)
        {
            var expectedSize = GeneralSizeEnum.ItemSizeInBytes.GetHashCode();

            if (data.Length == expectedSize)
                return data;

            var normalized = new byte[expectedSize];

            Buffer.BlockCopy(
                data,
                0,
                normalized,
                0,
                Math.Min(data.Length, expectedSize));

            Console.WriteLine(
                $"[ItemModel.ToArray] Normalized item packet size from {data.Length} to {expectedSize} bytes.");

            return normalized;
        }
    }
}