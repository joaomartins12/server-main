using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class DigimonSkillLimitOpenPacket : PacketWriter
    {
        private const int PacketNumber = 3245;

        /// <summary>
        /// Add skill max level.
        /// </summary>
        public DigimonSkillLimitOpenPacket(int nResult, int nEvoSlot, int itemSlot, int nItemType)
        {
            Type(PacketNumber);
            WriteInt(nResult);
        }
    }
}