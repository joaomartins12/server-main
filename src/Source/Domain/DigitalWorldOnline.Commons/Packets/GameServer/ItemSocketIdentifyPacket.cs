using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class ItemSocketIdentifyPacket : PacketWriter
    {
        private const int PacketNumber = 3929;

        public ItemSocketIdentifyPacket(ItemModel item, long Money) // ✅ ALTERADO para long
        {
            Type(PacketNumber);
            WriteByte(item.Power);
            WriteInt64(Money); // ✅ ALTERADO para WriteInt64
            WriteInt(0); // Esse provavelmente é um placeholder, pode deixar assim se for int mesmo
        }
    }
}
