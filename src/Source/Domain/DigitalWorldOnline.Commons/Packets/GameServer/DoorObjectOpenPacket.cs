using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer
{
    public class DoorObjectOpenPacket : PacketWriter
    {
        private const int PacketNumber = 16007;

        /// <summary>
        /// Set the target as out of combat.
        /// </summary>
        /// <param name="nFactID">The target handler to set</param>
        /// <param name="bOpend">Set the door openned/closed</param>
        public DoorObjectOpenPacket(int nFactID, byte bOpend)
        {
            Type(PacketNumber);
            WriteInt(nFactID);
            WriteByte(bOpend);
        }
    }
}
