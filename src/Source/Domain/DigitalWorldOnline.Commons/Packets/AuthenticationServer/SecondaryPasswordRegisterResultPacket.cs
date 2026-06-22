using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.AuthenticationServer
{
    public class SecondaryPasswordRegisterResultPacket : PacketWriter
    {
        private const int PacketNumber = 9801;

        /// <summary>
        /// Secondary password register result.
        /// 0 = success.
        /// Any non-zero value = client message/error code.
        /// </summary>
        public SecondaryPasswordRegisterResultPacket(int result)
        {
            Type(PacketNumber);
            WriteInt(result);
        }
    }
}