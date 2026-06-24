using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Utils;
using System.Text;

namespace DigitalWorldOnline.Commons.Models.Base
{
    public partial class ItemModel : ICloneable
    {
        private const int ClientItemSize = 68;

        public void SetPower(byte power) => Power = power;

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

        private void EnsureStatusLists()
        {
            if (AccessoryStatus == null)
                AccessoryStatus = new List<ItemAccessoryStatusModel>();

            if (SocketStatus == null)
                SocketStatus = new List<ItemSocketStatusModel>();

            for (byte i = 0; i < 8; i++)
            {
                if (!AccessoryStatus.Any(x => x.Slot == i))
                    AccessoryStatus.Add(new ItemAccessoryStatusModel(i));
            }

            AccessoryStatus = AccessoryStatus
                .OrderBy(x => x.Slot)
                .Take(8)
                .ToList();

            for (byte i = 0; i < 3; i++)
            {
                if (!SocketStatus.Any(x => x.Slot == i))
                    SocketStatus.Add(new ItemSocketStatusModel(i));
            }

            SocketStatus = SocketStatus
                .OrderBy(x => x.Slot)
                .Take(3)
                .ToList();
        }

        public uint RemainingSeconds()
        {
            if (ItemInfo == null)
                return 0;

            if (!ItemInfo.TemporaryItem)
                return 0;

            var time = (EndDate - DateTime.Now).TotalSeconds;

            if (time <= 0)
                return 0;

            return (uint)time;
        }

        public uint RemainingMinutes()
        {
            if (ItemInfo == null)
                return 0;

            if (!ItemInfo.TemporaryItem)
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
            if (ItemInfo == null)
                return 0;

            if (!ItemInfo.TemporaryItem)
                return 0;

            var time = (EndDate - DateTime.Now).TotalDays;

            if (time <= 0)
                return 0;

            return (uint)time;
        }

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

        public bool HasAccessoryStatus
        {
            get
            {
                EnsureStatusLists();
                return AccessoryStatus.Any(x => x.Value > 0);
            }
        }

        public bool HasSocketStatus
        {
            get
            {
                EnsureStatusLists();
                return SocketStatus.Any(x => x.Value > 0);
            }
        }

        public bool IsTemporary => ItemInfo?.UseTimeType > 0;

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

        public void SetItemId(int itemId = 0) => ItemId = itemId;

        public void SetRemainingTime(uint remainingTime = 0)
        {
            if (remainingTime == 4294967280)
                remainingTime = 0;

            Duration = (int)remainingTime;
            EndDate = DateTime.Now.AddMinutes(remainingTime);
        }

        public void SetAmount(int amount = 0) => Amount = amount;

        public void SetSlot(int slot) => Slot = slot;

        public void SetTradeSlot(int slot) => TradeSlot = slot;

        public void ReduceAmount(int amount) => Amount -= amount;

        public void SetSellPrice(long sellprice) => TamerShopSellPrice = sellprice;

        public void SetItemInfo(ItemAssetModel? info) => ItemInfo = info;

        public void SetFirstExpired(bool firstExpired) => FirstExpired = firstExpired;

        public void IncreaseAmount(int amount) => Amount += amount;

        public byte[] ToArray(bool simplified = false)
        {
            using var m = new MemoryStream();

            var itemPacketSize = GeneralSizeEnum.ItemSizeInBytes.GetHashCode();

            if (ItemId <= 0 || Amount <= 0)
            {
                for (int i = 0; i < itemPacketSize; i++)
                    m.WriteByte(0);

                return m.ToArray();
            }

            // Segurança: se o asset não foi carregado, nunca enviar item incompleto para o client.
            // Um ItemId sem ItemInfo pode fazer o client tentar usar um item que não existe no FileTable.
            if (ItemInfo == null)
            {
                Console.WriteLine(
                    $"[ItemModel.ToArray] ItemInfo NULL. Serializing empty slot. " +
                    $"ItemListId={ItemListId} Slot={Slot} ItemId={ItemId} Amount={Amount}"
                );

                for (int i = 0; i < itemPacketSize; i++)
                    m.WriteByte(0);

                return m.ToArray();
            }

            if (SocketStatus == null || SocketStatus.Count < 3)
            {
                SocketStatus = new List<ItemSocketStatusModel>
        {
            new ItemSocketStatusModel(0),
            new ItemSocketStatusModel(1),
            new ItemSocketStatusModel(2)
        };
            }

            if (AccessoryStatus == null || AccessoryStatus.Count < 8)
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

            m.Write(BitConverter.GetBytes(ItemId), 0, 4);
            m.Write(BitConverter.GetBytes(Amount), 0, 4);

            if (simplified)
            {
                m.Write(new byte[60]);
                return m.ToArray();
            }

            m.Write(BitConverter.GetBytes((short)0), 0, 2);
            m.Write(BitConverter.GetBytes((short)0), 0, 2);

            m.WriteByte(Power);
            m.WriteByte(RerollLeft);

            m.Write(BitConverter.GetBytes(ItemInfo.BoundType), 0, 2);

            foreach (var socketStatus in SocketStatus.OrderBy(x => x.Slot).Take(3))
                m.Write(BitConverter.GetBytes(socketStatus.AttributeId), 0, 2);

            foreach (var socketStatus in SocketStatus.OrderBy(x => x.Slot).Take(3))
                m.WriteByte((byte)Math.Clamp(socketStatus.Value, (short)0, (short)255));

            m.WriteByte(0);

            foreach (var accessoryStatus in AccessoryStatus.OrderBy(x => x.Slot).Take(8))
                m.Write(BitConverter.GetBytes(accessoryStatus.Type.GetHashCode()), 0, 2);

            foreach (var accessoryStatus in AccessoryStatus.OrderBy(x => x.Slot).Take(8))
                m.Write(BitConverter.GetBytes(accessoryStatus.Value), 0, 2);

            m.Write(BitConverter.GetBytes((short)0), 0, 2);

            var remaining = RemainingMinutes();

            if (remaining == 0 || remaining == 0xFFFFFFFF)
            {
                m.Write(BitConverter.GetBytes(0), 0, 4);
            }
            else
            {
                var ts = UtilitiesFunctions.RemainingTimeMinutes((int)remaining);
                m.Write(BitConverter.GetBytes(ts), 0, 4);
            }

            m.Write(BitConverter.GetBytes(0), 0, 4);

            var buffer = m.ToArray();

            // Garantir sempre o tamanho esperado pelo client.
            if (buffer.Length < itemPacketSize)
            {
                var fixedBuffer = new byte[itemPacketSize];
                Buffer.BlockCopy(buffer, 0, fixedBuffer, 0, buffer.Length);
                return fixedBuffer;
            }

            if (buffer.Length > itemPacketSize)
                return buffer.Take(itemPacketSize).ToArray();

            return buffer;
        }

        private static byte[] FixPacketSize(byte[] data, int size)
        {
            if (data.Length == size)
                return data;

            var result = new byte[size];

            if (data.Length > 0)
                Array.Copy(data, result, Math.Min(data.Length, size));

            return result;
        }

        public byte[] GiftToArray(bool simplified = false)
        {
            if (ItemId <= 0 || Amount <= 0)
                return Array.Empty<byte>();

            EnsureStatusLists();

            using MemoryStream m = new();
            using BinaryWriter writer = new(m);

            writer.Write(ItemId);
            writer.Write(Amount);

            if (simplified)
            {
                writer.Write(new byte[60]);
                return FixPacketSize(m.ToArray(), ClientItemSize);
            }

            writer.Write((short)0);
            writer.Write((short)0);

            writer.Write(Power);
            writer.Write(RerollLeft);

            writer.Write((short)(ItemInfo?.BoundType ?? 0));

            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write((short)0);

            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);

            foreach (var accessoryStatus in AccessoryStatus.OrderBy(x => x.Slot).Take(8))
                writer.Write((short)accessoryStatus.Type.GetHashCode());

            foreach (var accessoryStatus in AccessoryStatus.OrderBy(x => x.Slot).Take(8))
                writer.Write((short)accessoryStatus.Value);

            writer.Write((short)0);

            var remainingMinutes = RemainingMinutes();

            if (remainingMinutes == 4294967280 || remainingMinutes == 0xFFFFFFFF)
            {
                writer.Write(remainingMinutes);
            }
            else
            {
                writer.Write(UtilitiesFunctions.RemainingTimeMinutes((int)remainingMinutes));
            }

            writer.Write(0);

            return FixPacketSize(m.ToArray(), ClientItemSize);
        }

        public byte[] NewGiftToArray()
        {
            if (ItemId <= 0 || Amount <= 0)
                return Array.Empty<byte>();

            EnsureStatusLists();

            using MemoryStream m = new();
            using BinaryWriter writer = new(m);

            writer.Write(ItemId);
            writer.Write(Amount);

            writer.Write((short)0);
            writer.Write((short)0);

            writer.Write(Power);
            writer.Write(RerollLeft);

            writer.Write((short)(ItemInfo?.BoundType ?? 0));

            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write((short)0);

            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);

            foreach (var accessoryStatus in AccessoryStatus.OrderBy(x => x.Slot).Take(8))
                writer.Write((short)accessoryStatus.Type.GetHashCode());

            foreach (var accessoryStatus in AccessoryStatus.OrderBy(x => x.Slot).Take(8))
                writer.Write((short)accessoryStatus.Value);

            writer.Write((short)0);

            uint endTime = 0;

            if (EndDate > DateTime.MinValue)
                endTime = (uint)new DateTimeOffset(EndDate).ToUnixTimeSeconds();

            writer.Write(endTime);
            writer.Write(0);

            return FixPacketSize(m.ToArray(), ClientItemSize);
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

        public byte StatusAmount(AccessoryStatusTypeEnum type)
        {
            EnsureStatusLists();
            return (byte)AccessoryStatus.Count(x => x.Type == type);
        }

        public object Clone(Guid id)
        {
            var clonedObject = (ItemModel)Clone();
            clonedObject.Id = id;
            return clonedObject;
        }

        public object Clone()
        {
            var clone = (ItemModel)MemberwiseClone();

            clone.AccessoryStatus = new List<ItemAccessoryStatusModel>();

            if (AccessoryStatus != null)
            {
                foreach (var status in AccessoryStatus)
                {
                    clone.AccessoryStatus.Add(new ItemAccessoryStatusModel(status.Slot)
                    {
                        Id = status.Id,
                        Type = status.Type,
                        Value = status.Value,
                        ItemId = status.ItemId
                    });
                }
            }

            clone.SocketStatus = new List<ItemSocketStatusModel>();

            if (SocketStatus != null)
            {
                foreach (var status in SocketStatus)
                {
                    clone.SocketStatus.Add(new ItemSocketStatusModel(status.Slot)
                    {
                        Id = status.Id,
                        AttributeId = status.AttributeId,
                        Type = status.Type,
                        Value = status.Value,
                        ItemId = status.ItemId
                    });
                }
            }

            return clone;
        }
    }
}