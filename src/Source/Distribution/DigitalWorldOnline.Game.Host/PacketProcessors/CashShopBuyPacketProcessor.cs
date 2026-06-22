using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Application.Separar.Queries;
using MediatR;
using Serilog;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Packets.Chat;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using DigitalWorldOnline.Commons.Writers;
using System.Dynamic;
using System.Net.Sockets;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.GameHost;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class CashShopBuyPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.CashShopBuy;

        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;

        public CashShopBuyPacketProcessor(
            ILogger logger,
            AssetsLoader assets,
            ISender sender,
            MapServer mapServer)
        {
            _logger = logger;
            _assets = assets;
            _sender = sender;
            _mapServer = mapServer;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            int amount = packet.ReadByte();
            int price = packet.ReadInt();
            // skip type/u1 - not used currently
            packet.Skip(8);

            short Result = 1;
            sbyte TotalSuccess = 0;
            sbyte TotalFail = 0;

            // Quick inventory capacity check
            if (client.Tamer.Inventory.TotalEmptySlots < amount)
            {
                client.Send(new CashShopReturnPacket(31011, client.Premium, client.Silk, TotalSuccess, TotalFail));
                return;
            }

            if (client.Premium < price)
            {
                client.Send(new CashShopReturnPacket(31010, client.Premium, client.Silk, TotalSuccess, TotalFail));
                return;
            }

            // Cache CashShopAssets in a dictionary by Unique_Id for O(1) lookup
            var assetsDict = _assets.CashShopAssets?.ToDictionary(x => x.Unique_Id) ?? new Dictionary<int, CashShopAssetModel>();

            var successList = new List<int>();

            bool anyBought = false;

            for (int u = 0; u < amount; u++)
            {
                int unique = packet.ReadInt();

                if (!assetsDict.TryGetValue(unique, out var asset) || asset == null || asset.Activated != 1)
                {
                    // invalid
                    TotalFail++;
                    continue;
                }

                if (client.Premium < asset.Price)
                {
                    TotalFail++;
                    continue;
                }

                // Create item
                var itemId = asset.Item_Id;
                var itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId);
                var newItem = new ItemModel();
                newItem.SetItemInfo(itemInfo);
                newItem.ItemId = itemId;
                newItem.Amount = asset.Quanty;

                if (newItem.IsTemporary)
                    newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                // Try to add to inventory
                if (!client.Tamer.Inventory.AddItem(newItem))
                {
                    // if adding failed, fail this purchase
                    TotalFail++;
                    continue;
                }

                // Send receive packet (small packet) and collect DB update to send once after loop
                client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));

                client.Premium -= asset.Price;
                anyBought = true;
                TotalSuccess++;
                successList.Add(asset.Item_Id);
            }

            // Persist premium once
            await _sender.Send(new UpdatePremiumAndSilkCommand(client.Premium, client.Silk, client.AccountId));

            // If any items were added, batch update the inventory
            if (anyBought)
            {
                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                // Optionally log a summarized Discord message instead of per-item to reduce calls
                var summary = string.Join(", ", successList.Select(id => id.ToString()).Take(10));
                var message = $"**Compra no Cash Shop**\n**Jogador**: {client.Tamer.Name} comprou {TotalSuccess} item(s).\n**ItemIDs**: {summary}";

                // Fire-and-forget discord (don't await to avoid blocking)
                _ = Task.Run(() => _mapServer.CallDiscord(message, client, "00ff00", "Cash Shop", "1374552025395630130"));

                Result = 0;
            }

            client.Send(new CashShopReturnPacket(Result, client.Premium, client.Silk, TotalSuccess, TotalFail));
        }
    }
}
