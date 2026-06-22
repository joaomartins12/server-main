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

            WriteShort((short)accountWarehouse.Items.Count); // ✅ voltar ao original
            WriteBytes(accountWarehouse.Items.SelectMany(x => x.ToArray()).ToArray());
        }

    }
}