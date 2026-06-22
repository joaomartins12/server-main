using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.GameHost.EventsServer;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.GameServer;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class DigimonSkillLimitOpenPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.DigimonSkillOpen;

        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;

        public DigimonSkillLimitOpenPacketProcessor(ILogger logger, ISender sender, AssetsLoader assets, MapServer mapServer, DungeonsServer dungeonServer, EventServer eventServer, PvpServer pvpServer)
        {
            _logger = logger;
            _sender = sender;
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            int itemSlot = packet.ReadInt();
            int nItemType = packet.ReadInt();
            int nEvoSlot = packet.ReadInt();

            //_logger.Information($"InvSlot: {itemSlot}");
            //_logger.Information($"ItemType: {nItemType}");
            //_logger.Information($"EvoSlot: {nEvoSlot}");

            var inventoryItem = client.Tamer.Inventory.FindItemBySlot(itemSlot);

            var evoLine = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == client.Partner.BaseType)?.Lines.FirstOrDefault(x => x.Type == client.Partner.CurrentType);

            var nResult = 0;

            if (nResult != 0)
            {
                _logger.Error($"Skill Open Failed: {nResult}");
                return;
            }
            else
            {
                if (evoLine != null)
                {
                    //_logger.Information($"Digimon SlotLevel: {evoLine.SlotLevel}");

                    var Evolution = client.Tamer.Partner.Evolutions[evoLine.SlotLevel - 1];
                    //_logger.Information($"DigimonEvo Type: {Evolution.Type}");

                    if (Evolution != null)
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            Evolution.Skills[i].IncreaseMaxSkillLevel();
                        }

                        await _sender.Send(new UpdateEvolutionCommand(Evolution));

                        if (inventoryItem != null)
                        {
                            //_logger.Information($"InventoryItem: {inventoryItem.ItemId} - {inventoryItem.ItemInfo.Name}");

                            client.Tamer.Inventory.RemoveOrReduceItem(inventoryItem, 1, itemSlot);
                            client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));

                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                        }

                        await _sender.Send(new DigimonSkillLimitOpenPacket(nResult, nEvoSlot, itemSlot, nItemType));
                        //_logger.Information($"Packet Finished !!");
                    }
                }
            }
        }
    }
}