using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.Chat
{
    public class EncyclopediaDeckEffectPacket : PacketWriter
    {
        private const int PacketNumber = 3237;

        public EncyclopediaDeckEffectPacket(short deck, int EndTime)
        {
            Type(PacketNumber);
            WriteShort(deck);  
            WriteInt(EndTime);
        }
    }
}