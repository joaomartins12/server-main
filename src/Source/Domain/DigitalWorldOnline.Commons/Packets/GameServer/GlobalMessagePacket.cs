using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class GlobalMessagePacket : PacketWriter
    {
        private const int PacketNumber = 16006;


        /// <param name="targetHandler">Target handler</param>
        public GlobalMessagePacket(int targetHandler, string attackerName, int attackerType, int itemId)
        {
            Type(PacketNumber);
            WriteShort(1605);
            WriteInt(targetHandler);
            WriteString(attackerName);
            WriteInt(attackerType);
            WriteInt(itemId);
        }
        public GlobalMessagePacket(string attackerName, int itemId, int itemLevel)
        {
            Type(PacketNumber);
            WriteShort(1);
            WriteString(attackerName);
            WriteInt(itemId);
            WriteInt(itemLevel);
        }
        public GlobalMessagePacket(int targetHandler, int Map)
        {
            Type(PacketNumber);
            WriteShort(1606);
            WriteInt(targetHandler);
            WriteInt(Map);
        }

        public GlobalMessagePacket(int targetHandler, int Map, bool despawn)
        {
            Type(PacketNumber);
            WriteShort(1609);
            WriteInt(targetHandler);
            WriteInt(Map);
        }
        public GlobalMessagePacket(string message)
        {
            Type(PacketNumber);
            WriteShort(9999); // Código único para mensagem global (ajuste conforme necessário)
            WriteString(message);
        }

    }
}
