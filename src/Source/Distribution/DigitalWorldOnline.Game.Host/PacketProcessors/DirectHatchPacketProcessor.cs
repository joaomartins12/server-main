using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class DirectHatchPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.DirectHatch;

        private readonly StatusManager _statusManager;
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public DirectHatchPacketProcessor(
            StatusManager statusManager,
            AssetsLoader assets,
            MapServer mapServer,
            ILogger logger,
            ISender sender)
        {
            _statusManager = statusManager;
            _assets = assets;
            _mapServer = mapServer;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            try
            {

                var packet = new GamePacketReader(packetData);

                int itemType = packet.ReadInt();
                int inventorySlot = packet.ReadInt();
                string digimonName = packet.ReadString();
                client.blockAchievement = false;

                var item = client.Tamer.Inventory.FindItemBySlot(inventorySlot);
                if (item == null || item.ItemInfo?.ItemId != itemType)
                {
                    client.Send(new SystemMessagePacket($"Invalid item for hatching."));
                    return;
                }

                var hatchInfo = _assets.Hatchs.FirstOrDefault(x => x.ItemId == itemType);
                if (hatchInfo == null)
                {
                    client.Send(new SystemMessagePacket($"Unknown hatch info for item {itemType}."));
                    return;
                }

                byte i = 0;
                while (i < client.Tamer.DigimonSlots)
                {
                    if (client.Tamer.Digimons.FirstOrDefault(x => x.Slot == i) == null)
                        break;
                    i++;
                }
                var (hatchGrade, hatchSize) = GetHatchGradeAndSize(hatchInfo.HatchType);

                var newDigimon = DigimonModel.Create(
                  digimonName,
                  hatchInfo.HatchType,
                  hatchInfo.HatchType,
                  hatchGrade,
                  hatchSize,
                  i
              );

                newDigimon.NewLocation(client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y);

                newDigimon.SetBaseInfo(_statusManager.GetDigimonBaseInfo(newDigimon.BaseType));
                newDigimon.SetBaseStatus(_statusManager.GetDigimonBaseStatus(newDigimon.BaseType, newDigimon.Level, newDigimon.Size));

                if (newDigimon.BaseInfo == null || newDigimon.BaseStatus == null)
                {
                    client.Send(new SystemMessagePacket($"Unknown digimon info for {newDigimon.BaseType}."));
                    return;
                }

                newDigimon.AddEvolutions(_assets.EvolutionInfo.First(x => x.Type == newDigimon.BaseType));

                newDigimon.SetTamer(client.Tamer);
                client.Tamer.AddDigimon(newDigimon);

                var digimonInfo = await _sender.Send(new CreateDigimonCommand(newDigimon));
                if (digimonInfo != null)
                {
                    newDigimon.SetId(digimonInfo.Id);

                    var slot = -1;
                    foreach (var digimon in newDigimon.Evolutions)
                    {
                        slot++;

                        var evolution = digimonInfo.Evolutions[slot];

                        if (evolution != null)
                        {
                            digimon.SetId(evolution.Id);

                            var skillSlot = -1;
                            foreach (var skill in digimon.Skills)
                            {
                                skillSlot++;
                                var dtoSkill = evolution.Skills[skillSlot];
                                skill.SetId(dtoSkill.Id);
                            }
                        }
                    }
                }
                client.Tamer.Inventory.RemoveItem(item, (short)inventorySlot);

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));

                if (client.Partner?.GeneralHandler == null)
                {
                    return;
                }

                client.Send(new HatchFinishPacket(newDigimon, (ushort)(client.Partner.GeneralHandler + 1000), client.Tamer.Digimons.FindIndex(x => x == newDigimon)));

                if (_mapServer != null)
                {
                    if (newDigimon.HatchGrade == DigimonHatchGradeEnum.Perfect && newDigimon.Size >= 100)
                    {
                        _mapServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name, newDigimon.BaseType, newDigimon.Size).Serialize());
                    }
                }


            }
            catch (Exception ex)
            {
                client.Send(new SystemMessagePacket("An error occurred during the hatching process. Please try again."));
            }
        }
        private (DigimonHatchGradeEnum, short) GetHatchGradeAndSize(int hatchType)
        {
            return hatchType switch
            {
                669967 => (DigimonHatchGradeEnum.Transcend, (short)14000),
                75030 => (DigimonHatchGradeEnum.Transcend, (short)13500),
                75041 => (DigimonHatchGradeEnum.Transcend, (short)13500),
                //222 => (DigimonHatchGradeEnum.Transcend, (short)13500),
                //222 => (DigimonHatchGradeEnum.Transcend, (short)13500),

                _ => (DigimonHatchGradeEnum.Perfect, (short)12500),
            };
        }
    }

}
