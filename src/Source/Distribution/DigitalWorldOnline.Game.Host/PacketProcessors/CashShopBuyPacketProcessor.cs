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
        private readonly MapServer _mapServer;  // Assuming you need the MapServer for Discord logging

        public CashShopBuyPacketProcessor(
            ILogger logger,
            AssetsLoader assets,
            ISender sender,
            MapServer mapServer)  // Inject MapServer to access CallDiscord
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
            int type = packet.ReadInt();
            int u1 = packet.ReadInt();

            bool comprado = false;
            short Result = 1;
            sbyte TotalSuccess = 0;
            sbyte TotalFail = 0;

            int[] unique_id = new int[amount];
            List<int> success_id = new List<int>();
            List<int> fail_id = new List<int>();

            // Verificar se há espaço suficiente no inventário
            int requiredSlots = amount; // Cada item ocupa um slot
            if (client.Tamer.Inventory.TotalEmptySlots >= requiredSlots)
            {
                // Continue com a lógica existente
            }
            else
            {
                client.Send(new CashShopReturnPacket(31011, client.Premium, client.Silk, TotalSuccess, TotalFail)); // Código de erro para inventário cheio
                return;
            }

            if (client.Premium >= price)
            {
                for (int u = 0; u < amount; u++)
                {
                    unique_id[u] = packet.ReadInt();
                    var Quexi = _assets.CashShopAssets.FirstOrDefault(x => x.Unique_Id == unique_id[u]);
                    if (Quexi != null && Quexi.Activated == 1)
                    {
                        if (client.Premium >= Quexi.Price)
                        {
                            var itemId = Quexi.Item_Id;

                            var newItem = new ItemModel();
                            newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));

                            newItem.ItemId = itemId;
                            newItem.Amount = Quexi.Quanty;

                            if (newItem.IsTemporary)
                                newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                            var itemClone = (ItemModel)newItem.Clone();

                            if (client.Tamer.Inventory.AddItem(newItem))
                            {
                                client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory));
                                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                                // Log no Discord com a compra do jogador
                                var itemDetails = $"**{newItem.ItemInfo.Name}** x{newItem.Amount} (ID: {newItem.ItemId})";
                                var message = $"**Compra no Cash Shop**\n" +
                                              $"**Jogador**: {client.Tamer.Name} comprou um item na Cash Shop.\n" +
                                              $"**Item**: {itemDetails}\n" +
                                              $"**Preço**: {Quexi.Price} Caramelo Coin";

                                await _mapServer.CallDiscord(
                                    message,
                                    client,
                                    "00ff00",  // Cor de sucesso (verde)
                                    "Cash Shop",  // Local da ação
                                    "1374552025395630130" // ID do canal Discord (substitua pelo canal correto)
                                );

                                comprado = true;
                            }

                            Result = 0;
                            comprado = true;
                            success_id.Add(Quexi.Item_Id);
                            client.Premium -= Quexi.Price;
                        }
                    }
                    else
                    {
                        client.Send(new CashShopReturnPacket(31010, client.Premium, client.Silk, TotalSuccess, TotalFail));
                    }

                    if (Result == 1)
                    {
                        client.Send(new CashShopReturnPacket(31010, client.Premium, client.Silk, TotalSuccess, TotalFail));
                    }
                }

                await _sender.Send(new UpdatePremiumAndSilkCommand(client.Premium, client.Silk, client.AccountId));

                if (comprado == true)
                {
                    client.Send(new CashShopReturnPacket(Result, client.Premium, client.Silk, TotalSuccess, TotalFail));
                }
            }
            else
            {
                client.Send(new CashShopReturnPacket(31010, client.Premium, client.Silk, TotalSuccess, TotalFail));
            }
        }
    }
}
