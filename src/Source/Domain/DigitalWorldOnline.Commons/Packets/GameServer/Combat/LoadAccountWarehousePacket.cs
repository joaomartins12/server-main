using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Writers;

namespace DigitalWorldOnline.Commons.Packets.GameServer.Combat
{
    public class LoadAccountWarehousePacket : PacketWriter
    {
        private const int PacketNumber = 3930;

        /// </summary>
        /// <param name="accountWarehouse">The list of Cash Storage</param>
        public LoadAccountWarehousePacket(ItemListModel accountWarehouse)
        {
            Type(PacketNumber);

            if (accountWarehouse == null)
            {
                WriteShort(0);
                return;
            }

            WriteShort((short)accountWarehouse.Count);
            WriteBytes(accountWarehouse.ToArray());
        }
    }
}