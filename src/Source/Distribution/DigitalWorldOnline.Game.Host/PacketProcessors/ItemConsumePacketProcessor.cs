using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using MediatR;
using Microsoft.Extensions.Configuration;
using Serilog;
using DigitalWorldOnline.GameHost.EventsServer;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.DTOs.Config;
using DigitalWorldOnline.Application.Admin.Commands;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ItemConsumePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ConsumeItem;

        private const string GamerServerPublic = "GameServer:PublicAddress";
        private const string GameServerPort = "GameServer:Port";
        private readonly StatusManager _statusManager;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly AssetsLoader _assets;
        private readonly ConfigsLoader _configs;
        private readonly ExpManager _expManager;
        private readonly ISender _sender;
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        public ItemConsumePacketProcessor(
            StatusManager statusManager,
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            EventServer eventServer,
            PvpServer pvpServer,
            AssetsLoader assets,
            ExpManager expManager,
            ConfigsLoader configs,
            ISender sender,
            ILogger logger,
            IConfiguration configuration)
        {
            _statusManager = statusManager;
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _expManager = expManager;
            _assets = assets;
            _configs = configs;
            _sender = sender;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            packet.Skip(4);

            var itemSlot = packet.ReadShort();

            _logger.Debug($"Slot: {itemSlot}");

            if (client.Partner == null)
            {
                _logger.Warning($"Invalid partner for tamer id {client.TamerId}.");
                client.Send(new SystemMessagePacket($"Invalid partner."));
                return;
            }

            var targetItem = client.Tamer.Inventory.FindItemBySlot(itemSlot);

            if (targetItem == null)
            {
                _logger.Warning($"Invalid item at slot {itemSlot} for tamer id {client.TamerId}.");
                client.Send(new SystemMessagePacket($"Invalid item at slot {itemSlot}."));
                return;
            }

            var targetItemTrue = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == targetItem.ItemId);
            if (targetItemTrue.Section == 6230)
            {
                client.blockAchievement = false;
            }
            else
            {
                client.blockAchievement = true;
            }
            if (targetItemTrue == null)
            {
                client.Send(new SystemMessagePacket("Invalid item data."));
                return;
            }

            if (targetItem.ItemInfo == null || targetItem.ItemInfo.Name != targetItemTrue.Name || targetItem.ItemInfo.Id == 0)
            {
                client.Send(new SystemMessagePacket("Invalid item data."));
                return;
            }

            if (targetItem.ItemInfo.Type == 60)
            {
                if (targetItem.ItemInfo?.SkillInfo == null)
                {
                    client.Send(
                        UtilitiesFunctions.GroupPackets(
                            new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize(),
                            new SystemMessagePacket($"Invalid skill info for item id {targetItem.ItemId}.").Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                        )
                    );

                    _logger.Warning(
                        $"Invalid skill info for item id {targetItem.ItemId} and tamer id {client.TamerId}.");
                    return;
                }

                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

                foreach (var apply in targetItem.ItemInfo?.SkillInfo.Apply)
                {
                    switch (apply.Type)
                    {
                        case SkillCodeApplyTypeEnum.Default:
                            {
                                switch (apply.Attribute)
                                {
                                    case SkillCodeApplyAttributeEnum.EXP:
                                        {
                                            switch (targetItem.ItemInfo.Target)
                                            {
                                                case ItemConsumeTargetEnum.Both:
                                                    {
                                                        var value = Convert.ToInt64(apply.Value);

                                                        var result = _expManager.ReceiveTamerExperience(value, client.Tamer);
                                                        var result2 =
                                                            _expManager.ReceiveDigimonExperience(value, client.Tamer.Partner);

                                                        if (result.Success)
                                                        {
                                                            client.Send(
                                                                new ReceiveExpPacket(
                                                                    value,
                                                                    0,
                                                                    client.Tamer.CurrentExperience,
                                                                    client.Tamer.Partner.GeneralHandler,
                                                                    0,
                                                                    0,
                                                                    client.Tamer.Partner.CurrentExperience,
                                                                    0
                                                                )
                                                            );
                                                        }
                                                        else
                                                        {
                                                            client.Send(new SystemMessagePacket(
                                                                $"No proper configuration for tamer {client.Tamer.Model} leveling."));
                                                            return;
                                                        }

                                                        if (result.LevelGain > 0)
                                                        {
                                                            client.Tamer.SetLevelStatus(
                                                                _statusManager.GetTamerLevelStatus(
                                                                    client.Tamer.Model,
                                                                    client.Tamer.Level
                                                                )
                                                            );
                                                            switch (mapConfig?.Type)
                                                            {
                                                                case MapTypeEnum.Dungeon:
                                                                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.GeneralHandler,
                                                                            client.Tamer.Level).Serialize());
                                                                    break;

                                                                case MapTypeEnum.Event:
                                                                    _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.GeneralHandler,
                                                                            client.Tamer.Level).Serialize());
                                                                    break;

                                                                case MapTypeEnum.Pvp:
                                                                    _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.GeneralHandler,
                                                                            client.Tamer.Level).Serialize());
                                                                    break;

                                                                default:
                                                                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.GeneralHandler,
                                                                            client.Tamer.Level).Serialize());
                                                                    break;
                                                            }

                                                            client.Tamer.FullHeal();

                                                            client.Send(new UpdateStatusPacket(client.Tamer));
                                                        }

                                                        if (result.Success)
                                                            await _sender.Send(new UpdateCharacterExperienceCommand(client.TamerId,
                                                                client.Tamer.CurrentExperience, client.Tamer.Level));

                                                        if (result2.Success)
                                                        {
                                                            client.Send(
                                                                new ReceiveExpPacket(
                                                                    0,
                                                                    0,
                                                                    client.Tamer.CurrentExperience,
                                                                    client.Tamer.Partner.GeneralHandler,
                                                                    value,
                                                                    0,
                                                                    client.Tamer.Partner.CurrentExperience,
                                                                    0
                                                                )
                                                            );
                                                        }

                                                        if (result2.LevelGain > 0)
                                                        {
                                                            client.Partner.SetBaseStatus(
                                                                _statusManager.GetDigimonBaseStatus(
                                                                    client.Partner.CurrentType,
                                                                    client.Partner.Level,
                                                                    client.Partner.Size
                                                                )
                                                            );

                                                            switch (mapConfig?.Type)
                                                            {
                                                                case MapTypeEnum.Dungeon:
                                                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.Partner.GeneralHandler,
                                                                            client.Tamer.Partner.Level
                                                                        ).Serialize()
                                                                    );
                                                                    break;

                                                                case MapTypeEnum.Event:
                                                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.Partner.GeneralHandler,
                                                                            client.Tamer.Partner.Level
                                                                        ).Serialize()
                                                                    );
                                                                    break;

                                                                case MapTypeEnum.Pvp:
                                                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.Partner.GeneralHandler,
                                                                            client.Tamer.Partner.Level
                                                                        ).Serialize()
                                                                    );
                                                                    break;

                                                                default:
                                                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.Partner.GeneralHandler,
                                                                            client.Tamer.Partner.Level
                                                                        ).Serialize()
                                                                    );
                                                                    break;
                                                            }

                                                            client.Partner.FullHeal();

                                                            client.Send(new UpdateStatusPacket(client.Tamer));
                                                        }

                                                        if (result2.Success)
                                                            await _sender.Send(new UpdateDigimonExperienceCommand(client.Partner));
                                                    }

                                                    break;

                                                case ItemConsumeTargetEnum.Digimon:
                                                    {
                                                        var digimonResult =
                                                            _expManager.ReceiveDigimonExperience(apply.Value, client.Tamer.Partner);
                                                        var value = Convert.ToInt64(apply.Value);

                                                        if (digimonResult.Success)
                                                        {
                                                            client.Send(
                                                                new ReceiveExpPacket(
                                                                    0,
                                                                    0,
                                                                    client.Tamer.CurrentExperience,
                                                                    client.Tamer.Partner.GeneralHandler,
                                                                    value,
                                                                    0,
                                                                    client.Tamer.Partner.CurrentExperience,
                                                                    0
                                                                )
                                                            );
                                                        }

                                                        if (digimonResult.LevelGain > 0)
                                                        {
                                                            client.Partner.SetBaseStatus(
                                                                _statusManager.GetDigimonBaseStatus(
                                                                    client.Partner.CurrentType,
                                                                    client.Partner.Level,
                                                                    client.Partner.Size
                                                                )
                                                            );


                                                            switch (mapConfig?.Type)
                                                            {
                                                                case MapTypeEnum.Dungeon:
                                                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.Partner.GeneralHandler,
                                                                            client.Tamer.Partner.Level
                                                                        ).Serialize()
                                                                    );
                                                                    break;

                                                                case MapTypeEnum.Event:
                                                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.Partner.GeneralHandler,
                                                                            client.Tamer.Partner.Level
                                                                        ).Serialize()
                                                                    );
                                                                    break;

                                                                case MapTypeEnum.Pvp:
                                                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.Partner.GeneralHandler,
                                                                            client.Tamer.Partner.Level
                                                                        ).Serialize()
                                                                    );
                                                                    break;

                                                                default:
                                                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.Partner.GeneralHandler,
                                                                            client.Tamer.Partner.Level
                                                                        ).Serialize()
                                                                    );
                                                                    break;
                                                            }

                                                            client.Partner.FullHeal();

                                                            client.Send(new UpdateStatusPacket(client.Tamer));
                                                        }

                                                        if (digimonResult.Success)
                                                            await _sender.Send(new UpdateDigimonExperienceCommand(client.Partner));
                                                        break;
                                                    }

                                                case ItemConsumeTargetEnum.Tamer:
                                                    {
                                                        var value = Convert.ToInt64(apply.Value);

                                                        var result = _expManager.ReceiveTamerExperience(value, client.Tamer);
                                                        if (result.Success)
                                                        {
                                                            client.Send(
                                                                new ReceiveExpPacket(
                                                                    value,
                                                                    0,
                                                                    client.Tamer.CurrentExperience,
                                                                    client.Tamer.Partner.GeneralHandler,
                                                                    0,
                                                                    0,
                                                                    client.Tamer.Partner.CurrentExperience,
                                                                    0
                                                                )
                                                            );
                                                        }
                                                        else
                                                        {
                                                            client.Send(new SystemMessagePacket(
                                                                $"No proper configuration for tamer {client.Tamer.Model} leveling."));
                                                            return;
                                                        }

                                                        if (result.LevelGain > 0)
                                                        {
                                                            client.Tamer.SetLevelStatus(
                                                                _statusManager.GetTamerLevelStatus(
                                                                    client.Tamer.Model,
                                                                    client.Tamer.Level
                                                                )
                                                            );


                                                            switch (mapConfig?.Type)
                                                            {
                                                                case MapTypeEnum.Dungeon:
                                                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.GeneralHandler,
                                                                            client.Tamer.Level).Serialize());
                                                                    break;

                                                                case MapTypeEnum.Event:
                                                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.GeneralHandler,
                                                                            client.Tamer.Level).Serialize());
                                                                    break;

                                                                case MapTypeEnum.Pvp:
                                                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.GeneralHandler,
                                                                            client.Tamer.Level).Serialize());
                                                                    break;

                                                                default:
                                                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                                                        client.TamerId,
                                                                        new LevelUpPacket(
                                                                            client.Tamer.GeneralHandler,
                                                                            client.Tamer.Level).Serialize());
                                                                    break;
                                                            }

                                                            client.Tamer.FullHeal();

                                                            client.Send(new UpdateStatusPacket(client.Tamer));
                                                        }

                                                        if (result.Success)
                                                            await _sender.Send(new UpdateCharacterExperienceCommand(client.TamerId,
                                                                client.Tamer.CurrentExperience, client.Tamer.Level));
                                                    }
                                                    break;
                                            }
                                        }
                                        break;
                                }
                            }
                            break;
                    }
                }


                switch (mapConfig?.Type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonServer.BroadcastForTargetTamers(client.TamerId,
                            UtilitiesFunctions.GroupPackets(
                                new UpdateCurrentHPRatePacket(
                                    client.Tamer.GeneralHandler,
                                    client.Tamer.HpRate).Serialize(),
                                new UpdateCurrentHPRatePacket(
                                    client.Tamer.Partner.GeneralHandler,
                                    client.Tamer.Partner.HpRate).Serialize()
                            )
                        );
                        break;

                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTargetTamers(client.TamerId,
                            UtilitiesFunctions.GroupPackets(
                                new UpdateCurrentHPRatePacket(
                                    client.Tamer.GeneralHandler,
                                    client.Tamer.HpRate).Serialize(),
                                new UpdateCurrentHPRatePacket(
                                    client.Tamer.Partner.GeneralHandler,
                                    client.Tamer.Partner.HpRate).Serialize()
                            )
                        );
                        break;

                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTargetTamers(client.TamerId,
                            UtilitiesFunctions.GroupPackets(
                                new UpdateCurrentHPRatePacket(
                                    client.Tamer.GeneralHandler,
                                    client.Tamer.HpRate).Serialize(),
                                new UpdateCurrentHPRatePacket(
                                    client.Tamer.Partner.GeneralHandler,
                                    client.Tamer.Partner.HpRate).Serialize()
                            )
                        );
                        break;

                    default:
                        _mapServer.BroadcastForTargetTamers(client.TamerId,
                            UtilitiesFunctions.GroupPackets(
                                new UpdateCurrentHPRatePacket(
                                    client.Tamer.GeneralHandler,
                                    client.Tamer.HpRate).Serialize(),
                                new UpdateCurrentHPRatePacket(
                                    client.Tamer.Partner.GeneralHandler,
                                    client.Tamer.Partner.HpRate).Serialize()
                            )
                        );
                        break;
                }


                client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1, itemSlot);

                await _sender.Send(new UpdateItemCommand(targetItem));
                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                    )
                );

                return;
            }

            if (targetItem.ItemInfo.Type == 61)
            {
                await ConsumeFoodItem(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 62)
            {
                var SummonInfo = _assets.SummonInfo.FirstOrDefault(x => x.ItemId == targetItem.ItemId);

                if (SummonInfo != null)
                {
                    await SummonMonster(client, itemSlot, targetItem, SummonInfo);
                }
                else
                {
                    await ConsumeAchievement(client, itemSlot, targetItem);
                }
            }
            else if (targetItem.ItemInfo.Type == 63)
            {
                await BuffItem(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemId == 0)
            {
                await ConsumePremiumItem(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemId == 59063 ||
                     targetItem.ItemId == 59064 ||
                     targetItem.ItemId == 59065 ||
                     targetItem.ItemId == 59066)
            {
                await ResetSkill(client, itemSlot, targetItem);
            }
            else if (new List<int> { 57044, 57045, 57046, 57047, 57048, 57049, 57050, 57060, 57052 }.Contains(targetItem.ItemId)) // <- onde validBitItemIds é sua lista de itens válidos
            {
                await ConsumeBitsItem(client, itemSlot, targetItem);
            }

            else if (targetItem.ItemInfo.Section is 59016 or 59017)
            {
                await ConsumeMembershipVip(client, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 89 || targetItem.ItemInfo.Section == 9013)
            {
                await Fruits(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Section == 9011)
            {
                await Transcend(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Section == 9012)
            {
                await UltraAscension(client,itemSlot,targetItem);
            }
            else if (targetItem.ItemInfo.Type == 155)
            {
                await IncreaseInventorySlots(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 156)
            {
                await IncreaseWarehouseSlots(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 159)
            {
                await IncreaseDigimonSlots(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 160)
            {
                await IncreaseArchiveSlots(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 170 & targetItem.ItemInfo.Section == 17000)
            {
                await ContainerItem(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 180)
            {
                await CashTamerSkills(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 201)
            {
                await ConsumeFoodItem(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 71)
            {
                await ConsumeExpItem(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 72)
            {
                await BombTeleport(client, itemSlot, targetItem);
            }
            else if (targetItem.ItemInfo.Type == 170 & targetItem.ItemInfo.Section == 9400)
            {
                await HatchItem(client, itemSlot, targetItem);
            }
            else
                client.Send(new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type));
        }
        private async Task UltraAscension(GameClient client,short itemSlot,ItemModel targetItem)
        {

            var digimonLevel = client.Partner.HatchGrade;
            var digimonSize = client.Partner.Size;

            switch (digimonLevel)
            {
                case DigimonHatchGradeEnum.Transcend:
                    if (digimonSize < 13900)
                    {
                        await HandleFailure(client,itemSlot,targetItem);
                        return;
                    }
                    client.Tamer.Partner.Ascended();
                    client.Tamer.Partner.SetSize(14000);
                    break;

                //case DigimonHatchGradeEnum.Ascended:
                //    if (digimonSize < 14900)
                //    {
                //        await HandleFailure(client,itemSlot,targetItem);
                //        return;
                //    }

                //    client.Tamer.Partner.HighAscended();
                //    client.Tamer.Partner.SetSize(15000);
                //    break;

                //case DigimonHatchGradeEnum.HighAscended:
                //    if (digimonSize < 15900)
                //    {
                //        await HandleFailure(client,itemSlot,targetItem);
                //        return;
                //    }
                //    client.Tamer.Partner.UltraAscended();
                //    client.Tamer.Partner.SetSize(16000);
                //    break;

                //case DigimonHatchGradeEnum.UltraAscended:
                //    if (digimonSize < 16900)
                //    {
                //        await HandleFailure(client,itemSlot,targetItem);
                //        return;
                //    }
                //    client.Tamer.Partner.Ultimate();
                //    client.Tamer.Partner.SetSize(17000);
                //    break;

                default:
                    await HandleFailure(client,itemSlot,targetItem);
                    client.Send(new NoticeMessagePacket("Your digimon is at MAX level"));
                    return;

            }

            await HandleEvolution(client,itemSlot,targetItem);
        }
        private async Task HandleEvolution(GameClient client,short itemSlot,ItemModel targetItem)
        {
            client.Tamer.Inventory.RemoveOrReduceItem(targetItem,1);

            await _sender.Send(new UpdateItemCommand(targetItem));
            await _sender.Send(new UpdateDigimonSizeCommand(client.Partner.Id,client.Partner.Size));
            await _sender.Send(new UpdateDigimonGradeCommand(client.Tamer.Partner.Id,client.Tamer.Partner.HatchGrade));

            _mapServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale,client.Tamer.Name,
                                       client.Partner.BaseType,client.Partner.Size).Serialize());

            _dungeonServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale,client.Tamer.Name,
                                           client.Partner.BaseType,client.Partner.Size).Serialize());

            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,new UpdateSizePacket(client.Partner.GeneralHandler,client.Partner.Size).Serialize());

            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,new UpdateSizePacket(client.Partner.GeneralHandler,client.Partner.Size).Serialize());

            client.Send(UtilitiesFunctions.GroupPackets(
                                new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler,itemSlot).Serialize(),
                                new UpdateStatusPacket(client.Tamer).Serialize(),
                                new LoadInventoryPacket(client.Tamer.Inventory,InventoryTypeEnum.Inventory).Serialize()
                            )
                        );

            client.Send(new DigimonTranscendenceSuccessPacket(0,0,client.Tamer.Partner.HatchGrade,0,(int)client.Tamer.Inventory.Bits,0));
        }

        private Task HandleFailure(GameClient client,short itemSlot,ItemModel targetItem)
        {
            client.Send(
                UtilitiesFunctions.GroupPackets(
                    new ItemConsumeFailPacket(itemSlot,targetItem.ItemInfo.Type).Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory,InventoryTypeEnum.Inventory).Serialize()
                )
            );
            return Task.CompletedTask;
        }

        private async Task HatchItem(GameClient client, short itemSlot, ItemModel targetItem)
        {
            var hatchInfo = _assets.Hatchs.FirstOrDefault(x => x.ItemId == targetItem.ItemInfo.ItemId);

            if (hatchInfo == null)
            {
                _logger.Warning($"Unknown hatch info for egg {targetItem.ItemInfo.ItemId}.");
                client.Send(new SystemMessagePacket($"Unknown hatch info for egg {targetItem.ItemInfo.ItemId}."));
                return;
            }

            byte? digimonSlot = (byte)Enumerable.Range(0, client.Tamer.DigimonSlots)
                .FirstOrDefault(slot => client.Tamer.Digimons.FirstOrDefault(x => x.Slot == slot) == null);

            if (digimonSlot == null)
                return;

            var newDigimon = DigimonModel.Create("digiName", hatchInfo.HatchType, hatchInfo.HatchType,
                DigimonHatchGradeEnum.Perfect, 12500, (byte)digimonSlot);

            newDigimon.NewLocation(client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y);

            newDigimon.SetBaseInfo(_statusManager.GetDigimonBaseInfo(newDigimon.BaseType));

            newDigimon.SetBaseStatus(
                _statusManager.GetDigimonBaseStatus(newDigimon.BaseType, newDigimon.Level, newDigimon.Size));

            newDigimon.AddEvolutions(_assets.EvolutionInfo.First(x => x.Type == newDigimon.BaseType));

            if (newDigimon.BaseInfo == null || newDigimon.BaseStatus == null || !newDigimon.Evolutions.Any())
            {
                _logger.Warning($"Unknown digimon info for {newDigimon.BaseType}.");
                client.Send(new SystemMessagePacket($"Unknown digimon info for {newDigimon.BaseType}."));
                return;
            }

            newDigimon.SetTamer(client.Tamer);

            client.Tamer.AddDigimon(newDigimon);

            client.Send(new HatchFinishPacket(newDigimon, (ushort)(client.Partner.GeneralHandler + 1000),
                (byte)digimonSlot));

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

            client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1, itemSlot);
            await _sender.Send(new UpdateItemCommand(targetItem));

            client.Send(UtilitiesFunctions.GroupPackets(
                new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
        }

        private async Task CashTamerSkills(GameClient client, short itemSlot, ItemModel targetItem)
        {
            if (client.Tamer.TamerSkill.EquippedItems.Count == 5)
            {
                client.Send(new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type));
                return;
            }
            else
            {
                var targetSkill = _assets.TamerSkills.FirstOrDefault(x => x.SkillId == targetItem.ItemInfo?.SkillCode);

                if (targetSkill != null)
                {
                    targetItem.ItemInfo?.SetSkillInfo(
                        _assets.SkillCodeInfo.FirstOrDefault(x => x.SkillCode == targetSkill.SkillCode));
                }

                if (targetItem.ItemInfo?.SkillInfo == null)
                {
                    client.Send(
                        UtilitiesFunctions.GroupPackets(
                            new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize(),
                            new SystemMessagePacket($"Invalid skill info for item id {targetItem.ItemId}.").Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                        )
                    );

                    _logger.Warning(
                        $"Invalid skill info for item id {targetItem.ItemId} and tamer id {client.TamerId}.");
                    return;
                }

                var activeSkill =
                    client.Tamer.ActiveSkill.FirstOrDefault(x => x.SkillId == 0 || x.SkillId == targetSkill?.SkillId);
                if (activeSkill != null)
                {
                    if (activeSkill.SkillId == targetSkill?.SkillId)
                    {
                        activeSkill.IncreaseEndDate(targetItem.ItemInfo.UsageTimeMinutes);
                    }
                    else
                    {
                        activeSkill.SetTamerSkill(targetSkill.SkillId, 0, TamerSkillTypeEnum.Cash,
                            targetItem.ItemInfo.UsageTimeMinutes);
                    }
                }


                await _sender.Send(new UpdateTamerSkillCooldownByIdCommand(activeSkill));

                client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                        new ActiveTamerCashSkill(activeSkill.SkillId,
                            UtilitiesFunctions.RemainingTimeMinutes(activeSkill.RemainingMinutes)).Serialize()
                    )
                );


                return;
            }
        }

        private async Task SummonMonster(GameClient client, short itemSlot, ItemModel targetItem, SummonModel? SummonInfo)
        {
            if (!SummonInfo.Maps.Contains(client.Tamer.Location.MapId))
            {
                client.Send(new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type, ItemConsumeFailEnum.InvalidArea));
            }
            else
            {
                var count = 0;

                foreach (var mobToAdd in SummonInfo.SummonedMobs)
                {
                    count++;

                    var mob = (SummonMobModel)mobToAdd.Clone();

                    if (mob?.Location?.X != 0 && mob?.Location?.Y != 0)
                    {
                        var diff = UtilitiesFunctions.CalculateDistance(mob.Location.X, client.Tamer.Location.X, mob.Location.Y, client.Tamer.Location.Y);

                        if (diff > 5000)
                        {
                            client.Send(new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type, ItemConsumeFailEnum.InvalidArea));
                            break;
                        }
                        else if (count == 1)
                        {
                            client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                            client.Send(UtilitiesFunctions.GroupPackets(
                              new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                              new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                        }
                    }
                    else
                    {
                        client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);
                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));

                        client.Send(UtilitiesFunctions.GroupPackets(
                          new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                          new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                    }

                    int radius = 500; // Ajuste este valor para controlar a dispersão dos chefes
                    var random = new Random();

                    // Gerando valores aleatórios para deslocamento em X e Y
                    int xOffset = random.Next(-radius, radius + 1);
                    int yOffset = random.Next(-radius, radius + 1);

                    // Calculando as novas coordenadas do chefe de raid
                    int bossX = client.Tamer.Location.X + xOffset;
                    int bossY = client.Tamer.Location.Y + yOffset;

                    if (client.DungeonMap)
                    {
                        var map = _dungeonServer.Maps.FirstOrDefault(x => x.Clients.Exists(x => x.TamerId == client.TamerId));
                        var mobId = map.SummonMobs.Count + 1;

                        mob.SetId(mobId);

                        if (mob?.Location?.X != 0 && mob?.Location?.Y != 0)
                        {
                            bossX = mob.Location.X;
                            bossY = mob.Location.Y;

                            mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                        }
                        else
                        {
                            mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);

                        }

                        mob.SetDuration();
                        mob.SetTargetSummonHandle(client.Tamer.GeneralHandler);

                        _dungeonServer.AddSummonMobs(client.Tamer.Location.MapId, mob, client.TamerId);
                    }
                    else if (client.EventMap)
                    {
                        var map = _eventServer.Maps.FirstOrDefault(x => x.Clients.Exists(x => x.TamerId == client.TamerId));
                        var mobId = map.SummonMobs.Count + 1;

                        mob.SetId(mobId);

                        if (mob?.Location?.X != 0 && mob?.Location?.Y != 0)
                        {
                            bossX = mob.Location.X;
                            bossY = mob.Location.Y;

                            mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                        }
                        else
                        {
                            mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                        }

                        mob.SetDuration();
                        mob.SetTargetSummonHandle(client.Tamer.GeneralHandler);

                        _eventServer.AddSummonMobs(client.Tamer.Location.MapId, mob, client.TamerId);
                    }
                    else
                    {
                        var map = _mapServer.Maps.FirstOrDefault(x => x.MapId == client.Tamer.Location.MapId);
                        var mobId = map.SummonMobs.Count + 1;

                        mob.SetId(mobId);

                        if (mob?.Location?.X != 0 && mob?.Location?.Y != 0)
                        {
                            bossX = mob.Location.X;
                            bossY = mob.Location.Y;

                            mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                        }
                        else
                        {
                            mob.SetLocation(client.Tamer.Location.MapId, bossX, bossY);
                        }

                        mob.SetDuration();
                        mob.SetTargetSummonHandle(client.Tamer.GeneralHandler);

                        _mapServer.AddSummonMobs(client.Tamer.Location.MapId, mob);
                    }
                }
            }
        }

        private async Task ConsumeAchievement(GameClient client, short itemSlot, ItemModel targetItem)
        {
            client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);

            client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));

            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
        }

        private async Task BombTeleport(GameClient client, short itemSlot, ItemModel targetIte)
        {
            Dictionary<int, int> itemMapIdMapping = new Dictionary<int, int>
            {
                // Esse é um dicionário de ID de mapas e ID de itens
                {
                    25001, // ID do item
                    3 // ID do mapa
                },
                {
                    9025,
                    3
                },
                {
                    25003,
                    1100
                },
                {
                    9027,
                    1100
                },
                {
                    25006,
                    2100
                },
                {
                    25019,
                    2100
                },
                {
                    25004,
                    1103
                },
                {
                    9028,
                    1103
                }
            };
            // Verificar o ID do item e o ID do mapa que ele vai teleportar
            if (itemMapIdMapping.TryGetValue(targetIte.ItemId, out int mapId))
            {
                // if (client.Tamer.Location.MapId == mapId)
                // {
                //     client.Send(new SystemMessagePacket($"You are already in this map."));
                //     _logger.Error($"You are already in this map.");
                //     
                //     return;
                // }
                var waypoints = await _sender.Send(new MapRegionListAssetsByMapIdQuery(mapId));
                var destination = waypoints.Regions.First();

                // Ajusta os valores de X e Y com base no mapID
                switch (mapId)
                {
                    case 3:
                        destination.X = 19981;
                        destination.Y = 14501;
                        break;
                    case 1100:
                        destination.X = 21377;
                        destination.Y = 56675;
                        break;
                    case 2100:
                        destination.X = 9425;
                        destination.Y = 9680;
                        break;
                    case 1103:
                        destination.X = 4847;
                        destination.Y = 39008;
                        break;
                }

                client.Tamer.NewLocation(mapId, destination.X, destination.Y);
                await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));
                client.Tamer.Partner.NewLocation(mapId, destination.X, destination.Y);
                await _sender.Send(new UpdateDigimonLocationCommand(client.Tamer.Partner.Location));
                client.Tamer.UpdateState(CharacterStateEnum.Loading);
                await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));
                client.SetGameQuit(false);

                client.Send(new MapSwapPacket(
                        _configuration[GamerServerPublic],
                        _configuration[GameServerPort],
                        client.Tamer.Location.MapId,
                        client.Tamer.Location.X,
                        client.Tamer.Location.Y)
                    .Serialize());
                client.Tamer.Inventory.RemoveOrReduceItem(
                    targetIte,
                    1,
                    itemSlot
                );
                await _sender.Send(new UpdateItemCommand(targetIte));
            }
            else
            {
                Console.WriteLine($"ItemID {targetIte.ItemId} não encontrado no mapeamento.");
            }
        }

        private async Task Fruits(GameClient client, short itemSlot, ItemModel targetItem)
        {
            if (client.Partner != null)
            {
                var starterPartners = new List<int>() { 31001, 31002, 31003, 31004 };

                if (client.Partner.BaseType.IsBetween(  starterPartners.ToArray()))
                {
                    client.Send(new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize());
                    client.SendToAll(new NoticeMessagePacket($"Tamer: {client.Tamer.Name} tried to change starter digimon size using a cheat method, Then they got banned!").Serialize());

                    // var banProcessor = SingletonResolver.GetService<BanForCheating>();
                    // var banMessage = banProcessor.BanAccountWithMessage(client.AccountId, client.Tamer.Name, AccountBlockEnum.Permanent, "Cheating", client, "You tried to change your starter digimon size using a cheat method, So be happy with ban!");

                    // client.Send(new DisconnectUserPacket($"You tried to change starter digimon size using a cheat method, so be happy with Ban").Serialize());
                    return;
                }

            }

            var fruitConfig = _configs.Fruits.FirstOrDefault(x => x.ItemId == targetItem.ItemId);

            if (fruitConfig == null)
            {
                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize(),
                        new SystemMessagePacket($"Invalid fruit config for item {targetItem.ItemId}.").Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                    )
                );

                _logger.Error($"Invalid fruit config for item {targetItem.ItemId}.");
                return;
            }

            var newSizeList = fruitConfig.SizeList.Where(x =>
                x.HatchGrade == client.Partner.HatchGrade && x.MinSize > 0 && x.MaxSize > 0);

            if (!newSizeList.Any())
            {
                var sizeList = fruitConfig.SizeList.Where(x => x.HatchGrade == client.Partner.HatchGrade && x.Size > 1);

                if (!sizeList.Any())
                {
                    client.Send(
                        UtilitiesFunctions.GroupPackets(
                            new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize(),
                            new SystemMessagePacket(
                                    $"Invalid size list for fruit {targetItem.ItemId} and {client.Partner.HatchGrade} grade.")
                                .Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                        )
                    );

                    _logger.Error(
                        $"Invalid size list for fruit {targetItem.ItemId} and {client.Partner.HatchGrade} grade.");
                    return;
                }
                else
                {
                    var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
                    short newSize = 0;
                    var changeSize = false;
                    bool rare = false;

                    while (!changeSize)
                    {
                        var availableSizes = sizeList.Randomize();
                        foreach (var size in availableSizes)
                        {
                            if (size.Chance >= UtilitiesFunctions.RandomDouble())
                            {
                                rare = size.Size == availableSizes.Max(x => x.Size);

                                newSize = (short)(size.Size * 100);
                                changeSize = true;
                                break;
                            }
                        }
                    }

                    client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);

                    _logger.Verbose(
                        $"Character {client.TamerId} used {targetItem.ItemId} to change partner {client.Partner.Id} size from {client.Partner.Size / 100}% to {newSize / 100}%.");

                    client.Partner.SetSize(newSize);

                    switch (mapConfig?.Type)
                    {
                        case MapTypeEnum.Dungeon:
                            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateSizePacket(client.Partner.GeneralHandler, client.Partner.Size).Serialize());
                            break;

                        case MapTypeEnum.Event:
                            _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateSizePacket(client.Partner.GeneralHandler, client.Partner.Size).Serialize());
                            break;

                        case MapTypeEnum.Pvp:
                            _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateSizePacket(client.Partner.GeneralHandler, client.Partner.Size).Serialize());
                            break;

                        default:
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateSizePacket(client.Partner.GeneralHandler, client.Partner.Size).Serialize());
                            break;
                    }

                    client.Partner.SetBaseStatus(_statusManager.GetDigimonBaseStatus(
                        client.Partner.CurrentType, client.Partner.Level, client.Partner.Size));

                    if (rare)
                    {
                        _mapServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name,
                            client.Partner.BaseType, client.Partner.Size).Serialize());
                        _dungeonServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale,
                            client.Tamer.Name, client.Partner.BaseType, client.Partner.Size).Serialize());
                        _eventServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale,
                            client.Tamer.Name, client.Partner.BaseType, client.Partner.Size).Serialize());
                        _pvpServer.BroadcastGlobal(new NeonMessagePacket(NeonMessageTypeEnum.Scale,
                            client.Tamer.Name, client.Partner.BaseType, client.Partner.Size).Serialize());
                    }

                    await _sender.Send(new UpdateItemCommand(targetItem));
                    await _sender.Send(new UpdateDigimonSizeCommand(client.Partner.Id, client.Partner.Size));

                    client.Send(
                        UtilitiesFunctions.GroupPackets(
                            new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                            new UpdateStatusPacket(client.Tamer).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                        )
                    );
                }
            }
            else
            {
                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
                double newSize = 0;

                Random random = new Random();

                var availableSizes = newSizeList.FirstOrDefault();

                if (availableSizes == null)
                {
                    _logger.Error($"No size available on database !!");
                    return;
                }

                var minSize = availableSizes.MinSize * 100;
                var maxSize = availableSizes.MaxSize * 100;

                newSize = minSize + (random.NextDouble() * (maxSize - minSize));

                client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);

                _logger.Verbose(
                    $"Character {client.TamerId} used {targetItem.ItemId} to change partner {client.Partner.Id} size from {client.Partner.Size / 100}% to {newSize / 100}%.");

                client.Partner.SetSize((short)newSize);

                switch (mapConfig?.Type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UpdateSizePacket(client.Partner.GeneralHandler, client.Partner.Size).Serialize());
                        break;

                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UpdateSizePacket(client.Partner.GeneralHandler, client.Partner.Size).Serialize());
                        break;

                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UpdateSizePacket(client.Partner.GeneralHandler, client.Partner.Size).Serialize());
                        break;

                    default:
                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new UpdateSizePacket(client.Partner.GeneralHandler, client.Partner.Size).Serialize());
                        break;
                }

                client.Partner.SetBaseStatus(_statusManager.GetDigimonBaseStatus(
                    client.Partner.CurrentType, client.Partner.Level, client.Partner.Size));

                await _sender.Send(new UpdateItemCommand(targetItem));
                await _sender.Send(new UpdateDigimonSizeCommand(client.Partner.Id, client.Partner.Size));

                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                        new UpdateStatusPacket(client.Tamer).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                    )
                );
            }
        }

        private async Task Transcend(GameClient client,short itemSlot,ItemModel targetItem)
        {
            var evolutionType = _assets.DigimonBaseInfo
                .First(x => x.Type == client.Tamer.Partner.CurrentType)
                .EvolutionType;
            var digimonGrade = client.Partner.HatchGrade;
            var digimonSize = client.Partner.Size;

            if ((EvolutionRankEnum)evolutionType == EvolutionRankEnum.Spirit)
            {
                Console.WriteLine("Bypassing requirements due to Spirit Evolution!");

                await PerformTranscend(client,itemSlot,targetItem);
                return;
            }

            if (digimonGrade < DigimonHatchGradeEnum.Perfect || digimonSize < 12500)
            {
                client.Send(new SystemMessagePacket($"Digimon Level or Size not enough to transcend!",""));
                client.Send(new SystemMessagePacket($"Grade Required: 5\nSize Required: 125%",""));

                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemConsumeFailPacket(itemSlot,targetItem.ItemInfo.Type).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory,InventoryTypeEnum.Inventory).Serialize()
                    )
                );
                return;
            }

            if (digimonGrade == DigimonHatchGradeEnum.Transcend || digimonGrade > DigimonHatchGradeEnum.Transcend)
            {
                client.Send(new SystemMessagePacket($"Your Digimon is already transcended",""));

                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemConsumeFailPacket(itemSlot,targetItem.ItemInfo.Type).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory,InventoryTypeEnum.Inventory).Serialize()
                    )
                );
                return;
            }

            // Perform Transcend
            await PerformTranscend(client,itemSlot,targetItem);
        }
        private async Task PerformTranscend(GameClient client,short itemSlot,ItemModel targetItem)
        {
            Random random = new Random();
            int randomSize = random.Next(12500,13901);

            client.Partner.Transcend();
            client.Partner.SetSize((short)randomSize);

            client.Partner.SetBaseStatus(
                _statusManager.GetDigimonBaseStatus(client.Partner.CurrentType,client.Partner.Level,client.Partner.Size)
            );

            client.Tamer.Inventory.RemoveOrReduceItem(targetItem,1);

            await _sender.Send(new UpdateItemCommand(targetItem));
            await _sender.Send(new UpdateDigimonSizeCommand(client.Partner.Id,client.Partner.Size));
            await _sender.Send(new UpdateDigimonGradeCommand(client.Partner.Id,client.Partner.HatchGrade));

            client.Send(
                UtilitiesFunctions.GroupPackets(
                    new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler,itemSlot).Serialize(),
                    new UpdateStatusPacket(client.Tamer).Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory,InventoryTypeEnum.Inventory).Serialize()
                )
            );

            client.Send(new SystemMessagePacket(
                $"Tamer {client.Tamer.Name} used {targetItem.ItemInfo.Name} to transcend partner {client.Partner.Name} to {client.Partner.HatchGrade} with {client.Partner.Size / 100}% size.",
                ""
            ));

            _logger.Verbose(
                $"Tamer {client.Tamer.Name} used {targetItem.ItemInfo.Name} to transcend partner {client.Partner.Name} to {client.Partner.HatchGrade} with {client.Partner.Size / 100}% size."
            );

            client.Tamer.UpdateState(CharacterStateEnum.Loading);
            await _sender.Send(new UpdateCharacterStateCommand(client.TamerId,CharacterStateEnum.Loading));

            _mapServer.RemoveClient(client);
            client.SetGameQuit(false);

            client.Send(new MapSwapPacket(
                _configuration[GamerServerPublic],_configuration[GameServerPort],
                client.Tamer.Location.MapId,client.Tamer.Location.X,client.Tamer.Location.Y
            ));
        }

        private void SendFullStatusUpdate(GameClient client, MapConfigDTO mapConfig, bool updateHpDs = true, bool updateXGauge = false)
        {
            if (updateHpDs)
            {
                client.Send(new UpdateCurrentResourcesPacket(
                    client.Tamer.GeneralHandler,
                    (short)client.Tamer.CurrentHp,
                    (short)client.Tamer.CurrentDs,
                    0));

                client.Send(new UpdateStatusPacket(client.Tamer));

                var tamerRate = new UpdateCurrentHPRatePacket(
                    client.Tamer.GeneralHandler, client.Tamer.HpRate).Serialize();

                var partnerRate = new UpdateCurrentHPRatePacket(
                    client.Tamer.Partner.GeneralHandler, client.Tamer.Partner.HpRate).Serialize();

                var mapType = _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId)).Result;

                switch (mapType?.Type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonServer.BroadcastForTargetTamers(client.TamerId,
                            UtilitiesFunctions.GroupPackets(tamerRate, partnerRate));
                        break;

                    default:
                        _mapServer.BroadcastForTargetTamers(client.TamerId,
                            UtilitiesFunctions.GroupPackets(tamerRate, partnerRate));
                        break;
                }
            }

            if (updateXGauge)
            {
                client.Send(new TamerXaiResourcesPacket(client.Tamer.XGauge, client.Tamer.XCrystals));
            }
        }

        private async Task ConsumeFoodItem(GameClient client, short itemSlot, ItemModel targetItem)
        {
            if (targetItem.ItemInfo?.SkillInfo == null)
            {
                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize(),
                        new SystemMessagePacket($"Invalid skill info for item id {targetItem.ItemId}.").Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                    )
                );

                _logger.Error($"Invalid skill info for item id {targetItem.ItemId} and tamer id {client.TamerId}.");
                return;
            }

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
            bool updateHpDs = false;
            bool updateXGauge = false; // Declare the missing variable here

            foreach (var apply in targetItem.ItemInfo.SkillInfo.Apply)
            {
                switch (apply.Type)
                {
                    case SkillCodeApplyTypeEnum.Percent:
                    case SkillCodeApplyTypeEnum.AlsoPercent:
                        switch (apply.Attribute)
                        {
                            case SkillCodeApplyAttributeEnum.HP:
                                updateHpDs = true;
                                switch (targetItem.ItemInfo.Target)
                                {
                                    case ItemConsumeTargetEnum.Both:
                                        client.Tamer.RecoverHp((int)Math.Ceiling((double)apply.Value / 100 * client.Tamer.HP));
                                        client.Partner.RecoverHp((int)Math.Ceiling((double)apply.Value / 100 * client.Partner.HP));
                                        break;

                                    case ItemConsumeTargetEnum.Digimon:
                                        client.Partner.RecoverHp((int)Math.Ceiling((double)apply.Value / 100 * client.Partner.HP));
                                        break;

                                    case ItemConsumeTargetEnum.Tamer:
                                        client.Tamer.RecoverHp((int)Math.Ceiling((double)apply.Value / 100 * client.Tamer.HP));
                                        break;
                                }
                                break;

                            case SkillCodeApplyAttributeEnum.DS:
                                updateHpDs = true;
                                switch (targetItem.ItemInfo.Target)
                                {
                                    case ItemConsumeTargetEnum.Both:
                                        client.Tamer.RecoverDs((int)Math.Ceiling((double)apply.Value / 100 * client.Tamer.DS));
                                        client.Partner.RecoverDs((int)Math.Ceiling((double)apply.Value / 100 * client.Partner.DS));
                                        break;

                                    case ItemConsumeTargetEnum.Digimon:
                                        client.Partner.RecoverDs((int)Math.Ceiling((double)apply.Value / 100 * client.Partner.DS));
                                        break;

                                    case ItemConsumeTargetEnum.Tamer:
                                        client.Tamer.RecoverDs((int)Math.Ceiling((double)apply.Value / 100 * client.Tamer.DS));
                                        break;
                                }
                                break;
                        }
                        break;

                    case SkillCodeApplyTypeEnum.Default:
                        switch (apply.Attribute)
                        {
                            case SkillCodeApplyAttributeEnum.HP:
                                updateHpDs = true;
                                switch (targetItem.ItemInfo.Target)
                                {
                                    case ItemConsumeTargetEnum.Both:
                                        client.Tamer.RecoverHp(apply.Value);
                                        client.Partner.RecoverHp(apply.Value);
                                        break;

                                    case ItemConsumeTargetEnum.Digimon:
                                        client.Partner.RecoverHp(apply.Value);
                                        break;

                                    case ItemConsumeTargetEnum.Tamer:
                                        client.Tamer.RecoverHp(apply.Value);
                                        break;
                                }
                                break;

                            case SkillCodeApplyAttributeEnum.DS:
                                updateHpDs = true;
                                switch (targetItem.ItemInfo.Target)
                                {
                                    case ItemConsumeTargetEnum.Both:
                                        client.Tamer.RecoverDs(apply.Value);
                                        client.Partner.RecoverDs(apply.Value);
                                        break;

                                    case ItemConsumeTargetEnum.Digimon:
                                        client.Partner.RecoverDs(apply.Value);
                                        break;

                                    case ItemConsumeTargetEnum.Tamer:
                                        client.Tamer.RecoverDs(apply.Value);
                                        break;
                                }
                                break;

                            case SkillCodeApplyAttributeEnum.XG:
                                updateXGauge = true; // Set the variable when needed
                                client.Tamer.SetXGauge(apply.Value);
                                client.Send(new TamerXaiResourcesPacket(client.Tamer.XGauge, client.Tamer.XCrystals));
                                break;
                        }
                        break;
                }
            }

            SendFullStatusUpdate(client, mapConfig, updateHpDs, updateXGauge);

            bool removed = client.Tamer.Inventory.RemoveOrReduceItem(targetItem, (short)1);

            if (!removed)
            {
                client.Send(new SystemMessagePacket("Failed to consume item."));
                return;
            }

            await _sender.Send(new UpdateItemCommand(targetItem));

            client.Send(UtilitiesFunctions.GroupPackets(
                new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize()
            ));
        }

        private string FormatBitsAsReadable(long bits)
        {
            long teras = bits / 1_000_000;
            long megas = (bits % 1_000_000) / 1_000;

            string result = "";

            if (teras > 0)
                result += $"{teras} Tera{(teras > 1 ? "s" : "")}";

            if (megas > 0)
                result += (teras > 0 ? " e " : "") + $"{megas} Mega{(megas > 1 ? "s" : "")}";

            return string.IsNullOrWhiteSpace(result) ? "0 Bits" : result;
        }


        private async Task ConsumeBitsItem(GameClient client, short itemSlot, ItemModel targetItem)
        {
            // Tabela de valores por item
            var itemValues = new Dictionary<int, long>
            {
                { 57044, 300000 },     // 300 Mega
                { 57045, 100000 },     // 100 Mega
                { 57046, 3000000 },   // 3 Tera
                { 57047, 10000 },      // 10 Mega
                { 57048, 50000 },      // 50.000 Bits
                { 57049, 5000 },       // 5 Mega
                { 57050, 1000 },       // 1 Mega
                { 57060, 1000000 },   // 1 Tera
                { 57052, 10000000 }   // 10 Tera
            };

            if (!itemValues.ContainsKey(targetItem.ItemId))
            {
                client.Send(new SystemMessagePacket("This item is not valid for Bits."));
                return;
            }

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            if (mapConfig?.Type == MapTypeEnum.Dungeon || mapConfig?.Type == MapTypeEnum.Pvp)
            {
                client.Send(new SystemMessagePacket("You cannot use this item in this area."));
                return;
            }

            long bitsBefore = client.Tamer.Inventory.Bits;

            // Pega todos os itens válidos no inventário (que estão na tabela)
            var items = client.Tamer.Inventory.Items
                .Where(item => itemValues.ContainsKey(item.ItemId))
                .ToList();

            if (items.Count == 0)
            {
                client.Send(new SystemMessagePacket("You don't have any valid items to consume for Bits."));
                return;
            }

            long totalBitsToAdd = 0;
            int totalItemsConsumed = 0;

            foreach (var item in items)
            {
                long bitsPerItem = itemValues[item.ItemId];
                totalBitsToAdd += bitsPerItem * item.Amount;
                totalItemsConsumed += item.Amount;

                client.Tamer.Inventory.RemoveOrReduceItem(item, item.Amount, item.Slot);
                await _sender.Send(new UpdateItemCommand(item));
            }

            // Adiciona bits e atualiza no banco
            client.Tamer.Inventory.AddBits(totalBitsToAdd);
            await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory.Id, client.Tamer.Inventory.Bits));

            string FormatBits(long value)
            {
                if (value >= 1_000_000)
                    return $"{value / 1_000_000.0:#,0.##} Teras";
                else if (value >= 1_000)
                    return $"{value / 1_000.0:#,0.##} M";
                else
                    return $"{value:N0} Bits";
            }

            string formattedReward = FormatBits(totalBitsToAdd);

            // Pacotes
            client.Send(UtilitiesFunctions.GroupPackets(
                new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
               new SystemMessagePacket($"Voce recebeu {FormatBitsAsReadable(totalBitsToAdd)}!").Serialize()

            ));

            client.SetGameQuit(false);
            client.Tamer.UpdateSlots();

            // Log Discord
            var message = $"**Bits Item Consumido**\n" +
                          $"Tamer: {client.Tamer.Name}\n" +
                          $"Total de Itens Consumidos: {totalItemsConsumed}\n" +
                          $"Bits Antes: {bitsBefore:N0}\n" +
                          $"Bits Depois: {client.Tamer.Inventory.Bits:N0}\n" +
                          $"Recebido: {formattedReward}";

            await _mapServer.CallDiscord(
                message,
                client,
                "00CED1",
                "BITS USADO",
                "1374551492156985384"
            );
        }



        private async Task IncreaseArchiveSlots(GameClient client, short itemSlot, ItemModel targetItem)
        {
            client.Tamer.DigimonArchive.AddSlot();

            _logger.Verbose(
                $"Character {client.TamerId} used {targetItem.ItemId} to expand digimon archive slots to {client.Tamer.DigimonArchive.Slots}.");

            client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);

            await _sender.Send(new UpdateItemCommand(targetItem));
            await _sender.Send(new CreateCharacterDigimonArchiveSlotCommand(
                    client.Tamer.DigimonArchive.DigimonArchives.Last(),
                    client.Tamer.DigimonArchive.Id
                )
            );

            client.Send(
                UtilitiesFunctions.GroupPackets(
                    new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                    new DigimonArchiveLoadPacket(client.Tamer.DigimonArchive).Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                )
            );
        }

        private async Task IncreaseDigimonSlots(GameClient client, short itemSlot, ItemModel targetItem)
        {
            client.Tamer.AddDigimonSlots();

            _logger.Verbose(
                $"Character {client.TamerId} used {targetItem.ItemId} to expand digimon slots to {client.Tamer.DigimonSlots}.");

            client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);

            await _sender.Send(new UpdateCharacterDigimonSlotsCommand(client.Tamer.Id, client.Tamer.DigimonSlots));
            await _sender.Send(new UpdateItemCommand(targetItem));

            client.Send(
                UtilitiesFunctions.GroupPackets(
                    new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                    new UpdateDigimonSlotsPacket(client.Tamer.DigimonSlots).Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                )
            );
        }

        private async Task IncreaseWarehouseSlots(GameClient client, short itemSlot, ItemModel targetItem)
        {
            int maxSize = (int)GeneralSizeEnum.WarehouseMax; // 350

            // capacidade disponível agora
            int available = maxSize - client.Tamer.Warehouse.Size;
            if (available <= 0)
            {
                _logger.Warning($"Character {client.TamerId} tried to expand warehouse but already reached the limit of {maxSize} slots.");
                return;
            }

            // quantos itens/slots realmente podemos tentar usar nesta ação
            int remainingItems = Math.Min((int)targetItem.Amount, available);

            const int CHUNK = 255; // pode trocar para 100 se preferires
            var createdAll = new List<ItemModel>();
            int used = 0;

            while (remainingItems > 0)
            {
                // recalc do cap a cada iteração (Size pode ter sido alterado pelo AddSlotsAll/gravação)
                int capNow = maxSize - client.Tamer.Warehouse.Size;
                if (capNow <= 0) break;

                int stepInt = Math.Min(remainingItems, capNow);
                stepInt = Math.Min(stepInt, CHUNK);
                byte step = (byte)stepInt;
                if (step == 0) break;

                var created = client.Tamer.Warehouse.AddSlotsAll(step); // método original (byte)
                if (created != null && created.Count > 0)
                {
                    createdAll.AddRange(created);
                    used += step;
                    remainingItems -= step;
                }
                else
                {
                    _logger.Warning($"[Warehouse] AddSlotsAll não criou slots no step={step}. Abortando para evitar desync.");
                    break;
                }
            }

            if (used <= 0)
            {
                _logger.Warning($"Character {client.TamerId} tried to expand warehouse but already reached the limit of {maxSize} slots.");
                return;
            }

            // consome apenas o que foi realmente usado
            int left = used;
            while (left > 0)
            {
                byte step = (byte)Math.Min(left, CHUNK);
                client.Tamer.Inventory.RemoveOrReduceItem(targetItem, step);
                left -= step;
            }

            await _sender.Send(new UpdateItemCommand(targetItem));
            await _sender.Send(new AddInventorySlotsCommand(createdAll));

            client.Send(UtilitiesFunctions.GroupPackets(
                new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                new LoadInventoryPacket(client.Tamer.Warehouse, InventoryTypeEnum.Warehouse).Serialize(),
                new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
            ));
        }

        private async Task ConsumePremiumItem(GameClient client, short itemSlot, ItemModel targetItem)
        {
            if (targetItem.ItemId != 0)
            {
                client.Send(new SystemMessagePacket("This item is not valid for Premium."));
                return;
            }

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            if (mapConfig?.Type == MapTypeEnum.Dungeon || mapConfig?.Type == MapTypeEnum.Pvp)
            {
                client.Send(new SystemMessagePacket("You cannot use this item in this area."));
                return;
            }

            // Pega o total de Premium antes de consumir o item
            int premiumBefore = client.Premium;

            // Pega todos os itens de ID 9337 no inventário
            var items = client.Tamer.Inventory.FindItemsById(9337);
            int totalItems = items.Sum(x => x.Amount);

            if (totalItems == 0)
            {
                client.Send(new SystemMessagePacket("You don't have any valid items to consume for Premium."));
                return;
            }

            // Adiciona o Premium equivalente à quantidade de itens consumidos
            client.AddPremium(totalItems);

            // Atualiza o banco de dados
            await _sender.Send(new UpdatePremiumAndSilkCommand(client.Premium, client.Silk, client.AccountId));

            // Remover todos os itens de ID 9337 do inventário
            foreach (var item in items)
            {
                int removeAmount = item.Amount;
                client.Tamer.Inventory.RemoveOrReduceItem(item, removeAmount, item.Slot);
                await _sender.Send(new UpdateItemCommand(item));
            }

            // Enviar pacotes de atualização para o jogador
            client.Send(UtilitiesFunctions.GroupPackets(
                new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                new SystemMessagePacket($"You received +{totalItems} Premium!").Serialize()
            ));

            client.SetGameQuit(false);
            client.Tamer.UpdateSlots();

            client.Send(new MapSwapPacket(
                _configuration[GamerServerPublic],
                _configuration[GameServerPort],
                client.Tamer.Location.MapId,
                client.Tamer.Location.X,
                client.Tamer.Location.Y
            ));

            // Adicionando log no Discord
            var message = $"**Premium Item Consumido**\n" +
                          $"Tamer: {client.Tamer.Name}\n" +
                          $"Item: Premium Medal x{totalItems} (ID: 9337)\n" +
                          $"Premium Antes: {premiumBefore}\n" +
                          $"Premium Depois: {client.Premium}";

            await _mapServer.CallDiscord(
                message,
                client,
                "FFD700",  // Cor dourada para representar premium
                "PREMIUM USADO",  // Título do log
                "1354695607167619072" // ID do canal Discord (substitua pelo correto)
            );
        }

        private async Task ResetSkill(GameClient client, short itemSlot, ItemModel targetItem)
        {
            int itemId = targetItem.ItemId;
            int requiredMaxLevel = 0;
            int setLevel = 0;
            string itemName = "";
            string discordTitle = "";
            string discordColor = "";
            string successMessage = "";

            // Definições por item
            switch (itemId)
            {
                case 59063:
                    requiredMaxLevel = 10;
                    setLevel = 10;
                    itemName = "Resetador de Skill (ID: 59063)";
                    discordTitle = "SKILL MAX LEVEL 10";
                    discordColor = "1E90FF";
                    successMessage = "O level máximo das skills foi alterado para 10 e todas as skills foram definidas para o nível 10!";
                    break;
                case 59064:
                    requiredMaxLevel = 15;
                    setLevel = 15;
                    itemName = "Resetador de Skill (ID: 59064)";
                    discordTitle = "SKILL MAX LEVEL 15";
                    discordColor = "32CD32";
                    successMessage = "O level máximo das skills foi alterado para 15 e todas as skills foram definidas para o nível 15!";
                    break;
                case 59065:
                    requiredMaxLevel = 20;
                    setLevel = 20;
                    itemName = "Resetador de Skill (ID: 59065)";
                    discordTitle = "SKILL MAX LEVEL 20";
                    discordColor = "FFD700";
                    successMessage = "O level máximo das skills foi alterado para 20 e todas as skills foram definidas para o nível 20!";
                    break;
                case 59066:
                    requiredMaxLevel = 25;
                    setLevel = 25;
                    itemName = "Resetador de Skill (ID: 59066)";
                    discordTitle = "SKILL MAX LEVEL 25";
                    discordColor = "FFD700";
                    successMessage = "O level máximo das skills foi alterado para 20 e todas as skills foram definidas para o nível 25!";
                    break;
                default:
                    client.Send(new SystemMessagePacket("Este item não pode ser usado para resetar as skills."));
                    return;
            }

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
            if (mapConfig?.Type == MapTypeEnum.Dungeon || mapConfig?.Type == MapTypeEnum.Pvp)
            {
                client.Send(new SystemMessagePacket("Você não pode usar este item nesta área."));
                return;
            }

            var evoLine = _assets.EvolutionInfo
                .FirstOrDefault(x => x.Type == client.Partner.BaseType)
                ?.Lines.FirstOrDefault(x => x.Type == client.Partner.CurrentType);

            if (evoLine == null)
            {
                client.Send(new SystemMessagePacket("Evolução não encontrada."));
                return;
            }

            var evolution = client.Tamer.Partner.Evolutions.ElementAtOrDefault(evoLine.SlotLevel - 1);
            if (evolution == null)
            {
                client.Send(new SystemMessagePacket("Não foi possível obter os dados de skill."));
                return;
            }

            // Verifica se todas as skills estão no maxLevel requerido
            bool allSkillsAtRequiredMax = evolution.Skills.All(skill => skill != null && skill.MaxLevel == requiredMaxLevel);
            if (!allSkillsAtRequiredMax)
            {
                client.Send(new SystemMessagePacket($"Todas as skills precisam estar com o level máximo {requiredMaxLevel} para usar este item."));
                return;
            }

            // Maximiza todas as skills para o novo valor
            foreach (var skill in evolution.Skills)
            {
                if (skill != null)
                {
                    if (skill.MaxLevel != setLevel)
                    {
                        skill.IncreaseMaxSkillLevel((byte)(setLevel - skill.MaxLevel));
                    }
                    skill.IncreaseSkillLevel((byte)(setLevel - skill.CurrentLevel));
                }
            }

            await _sender.Send(new UpdateEvolutionCommand(evolution));

            // Consome 1 item
            client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1, itemSlot);
            await _sender.Send(new UpdateItemCommand(targetItem));

            // Envia feedback ao jogador
            client.Send(UtilitiesFunctions.GroupPackets(
                new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                new SystemMessagePacket(successMessage).Serialize()
            ));

            // Loga no Discord
            var message = $"**Upgrade de Skill (MaxLevel {setLevel} e Level {setLevel})**\n" +
                          $"Tamer: {client.Tamer.Name}\n" +
                          $"Digimon: {client.Tamer.Partner.Name} (Type: {client.Tamer.Partner.BaseType})\n" +
                          $"Item: {itemName}\n" +
                          $"EvoSlot: {evoLine.SlotLevel}";

            await _mapServer.CallDiscord(
                message,
                client,
                discordColor,
                discordTitle,
                "1374551559534284892"
            );

            // Reload do jogador
            client.Tamer.UpdateState(CharacterStateEnum.Loading);
            await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));
            _mapServer.RemoveClient(client);
            client.SetGameQuit(false);
            client.Tamer.UpdateSlots();
            client.Send(new MapSwapPacket(
                _configuration[GamerServerPublic],
                _configuration[GameServerPort],
                client.Tamer.Location.MapId,
                client.Tamer.Location.X,
                client.Tamer.Location.Y
            ));
        }

        private async Task IncreaseInventorySlots(GameClient client, short itemSlot, ItemModel targetItem)
        {
            int maxInventorySize = (int)GeneralSizeEnum.InventoryMax; // ex.: 150
            int available = maxInventorySize - client.Tamer.Inventory.Size;

            if (available <= 0)
            {
                _logger.Warning($"Character {client.TamerId} tried to expand inventory but already reached the limit of {maxInventorySize} slots.");
                return;
            }

            // só tentamos usar até ao limite disponível
            int remainingItems = Math.Min((int)targetItem.Amount, available);

            const int CHUNK = 255; // manter byte nas chamadas AddSlotsAll/RemoveOrReduceItem
            var createdAll = new List<ItemModel>();
            int used = 0;

            while (remainingItems > 0)
            {
                // recalc do cap a cada passo (evita “só abre 1” e depois dá limite)
                int capNow = maxInventorySize - client.Tamer.Inventory.Size;
                if (capNow <= 0) break;

                int stepInt = Math.Min(remainingItems, capNow);
                stepInt = Math.Min(stepInt, CHUNK);
                byte step = (byte)stepInt;
                if (step == 0) break;

                var created = client.Tamer.Inventory.AddSlotsAll(step); // método original (byte)
                if (created != null && created.Count > 0)
                {
                    createdAll.AddRange(created);
                    used += step;
                    remainingItems -= step;
                }
                else
                {
                    _logger.Warning($"[Inventory] AddSlotsAll did not create slots on step={step} for Tamer {client.TamerId}. Aborting to avoid desync.");
                    break;
                }
            }

            if (used <= 0)
            {
                _logger.Warning($"Character {client.TamerId} tried to expand inventory but already reached the limit of {maxInventorySize} slots.");
                return;
            }

            // consome exatamente o que foi usado, também em lotes de byte
            int left = used;
            while (left > 0)
            {
                byte step = (byte)Math.Min(left, CHUNK);
                client.Tamer.Inventory.RemoveOrReduceItem(targetItem, step);
                left -= step;
            }

            await _sender.Send(new UpdateItemCommand(targetItem));
            await _sender.Send(new AddInventorySlotsCommand(createdAll));

            client.Send(
                UtilitiesFunctions.GroupPackets(
                    new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                )
            );
        }

        private async Task ConsumeMembershipVip(GameClient client, ItemModel targetItem)
        {
            // ---------------------------
            // Regras de Membership VIP:
            // Sections 59016 e 59017
            // - Section 59016 possui os itens: 59016, 59017, 59018
            // - Section 59017 possui os itens: 59019, 59020, 59021
            //
            // O Section 59016 concede VIP LV 1 e o Section 59017, VIP LV 2.
            // Duração:
            // - Itens 59016 e 59019: 30 dias (60 * 60 * 24 * 30)
            // - Itens 59017 e 59020: 60 dias (60 * 60 * 24 * 60)
            // - Itens 59018 e 59021: 90 dias (60 * 60 * 24 * 90)
            // ---------------------------

            // 1. Validação: O item deve pertencer à seção 59016 ou 59017
            if (targetItem.ItemInfo.Section is not 59016 and not 59017)
            {
                client.Send(new SystemMessagePacket("This item is not valid for Membership."));
                return;
            }

            // 2. Determina o nível VIP com base na seção:
            // Se for 59017, o VIP level é 2; caso contrário (59016) é 1.
            int vipLevel = targetItem.ItemInfo.Section == 59017 ? 2 : 1;

            // 3. Determina a duração da Membership em segundos com base no ItemId
            // (supondo que 'targetItem.ItemInfo.ItemId' seja o identificador do item):
            int membershipDays = 0;
            switch (targetItem.ItemInfo.ItemId)
            {
                case 59016:
                case 59019:
                    membershipDays = 30;
                    break;
                case 59017:
                case 59020:
                    membershipDays = 60;
                    break;
                case 59018:
                case 59021:
                    membershipDays = 90;
                    break;
                default:
                    client.Send(new SystemMessagePacket("You cannot use this item now."));
                    return;
                    break;
            }
            int membershipTimeInSeconds = 60 * 60 * 24 * membershipDays;

            if (client.AccessLevel == AccountAccessLevelEnum.Vip && targetItem.ItemInfo.Section == 59017)
            {
                client.Send(new SystemMessagePacket("You can't use this item now. Wait your actually vip finish first."));
                return;
            }

            if (client.AccessLevel == AccountAccessLevelEnum.Vip2 && targetItem.ItemInfo.Section == 59016)
            {
                client.Send(new SystemMessagePacket("You can't use this item now. Wait your actually vip finish first."));
                return;
            }

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
            if (mapConfig?.Type != MapTypeEnum.Default)
            {
                client.Send(new SystemMessagePacket("You cannot use this item in this area."));
                return;
            }

            if (targetItem.Amount < 1)
            {
                client.Send(new SystemMessagePacket("You don't have any valid items to consume for Premium."));
                return;
            }

            var buffData = new List<(int BuffId, int Value1, int Value2)>
{
    (50121, 2700022, 2592000),
    (50122, 2700023, 2592000),
    (50123, 2700024, 2592000)
};

            foreach (var (buffId, value1, value2) in buffData)
            {
                var buff = _assets.BuffInfo.FirstOrDefault(x => x.SkillCode == value1);
                if (buff == null) continue;

                var existingBuff = client.Tamer.BuffList.Buffs.FirstOrDefault(x => x.BuffId == buffId);
                if (existingBuff == null)
                {
                    var newBuff = CharacterBuffModel.Create(buffId, value1, value2);
                    newBuff.SetBuffInfo(buff);
                    client.Tamer.BuffList.Add(newBuff);

                    var duration = UtilitiesFunctions.RemainingTimeSeconds(value2);
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new AddBuffPacket(client.Tamer.GeneralHandler, buff, 0, duration).Serialize());
                }
                else
                {
                    existingBuff.SetDuration(value2);
                    var duration = UtilitiesFunctions.RemainingTimeSeconds(existingBuff.Duration);
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new UpdateBuffPacket(client.Tamer.GeneralHandler, buff, 0, duration).Serialize());
                }
            }

            client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);
            client.IncreaseMembershipDuration(membershipTimeInSeconds);
            var expiration = client.MembershipExpirationDate!.Value;

            await Task.WhenAll(
                _sender.Send(new UpdateAccountMembershipCommand(client.AccountId, expiration)),
                _sender.Send(new UpdateItemCommand(targetItem)),
                _sender.Send(new UpdateAccessCommand(client.Tamer.AccountId, (UserAccessLevelEnum)vipLevel)),
                _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList))
            );

            client.Tamer.UpdateState(CharacterStateEnum.Loading);
            await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

            // Remove o cliente do servidor apropriado conforme o tipo do mapa
            _mapServer.RemoveClient(client);

            client.SetGameQuit(false);
            client.Tamer.UpdateSlots();

            // Envia o pacote de troca de mapa
            client.Send(new MapSwapPacket(_configuration[GamerServerPublic], _configuration[GameServerPort], client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y));
        }

        private async Task ContainerItem(GameClient client, short itemSlot, ItemModel targetItem)
        {
            var containerItem = client.Tamer.Inventory.FindItemBySlot(itemSlot);
            var ItemId = 0;

            if (containerItem == null || containerItem.ItemId == 0 || containerItem.ItemInfo == null)
            {
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo?.Type ?? 0).Serialize(),
                    new SystemMessagePacket($"Invalid item on slot {itemSlot} for tamer {client.TamerId}").Serialize()
                ));
                _logger.Warning($"Invalid item on slot {itemSlot} for tamer {client.TamerId}.");
                return;
            }

            var containerAsset = _assets.Container.FirstOrDefault(x => x.ItemId == containerItem.ItemId);
            if (containerAsset == null)
            {
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo?.Type ?? 0).Serialize(),
                    new SystemMessagePacket($"No container configuration for item id {containerItem.ItemId}.").Serialize()
                ));
                _logger.Warning($"No container configuration for item id {containerItem.ItemId}");
                return;
            }

            if (!containerAsset.Rewards.Any())
            {
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo?.Type ?? 0).Serialize(),
                    new SystemMessagePacket($"Container config for item {containerAsset.ItemId} has incorrect rewards configuration.").Serialize()
                ));
                _logger.Warning($"Container config for item {containerAsset.ItemId} has incorrect rewards configuration.");
                return;
            }

            var receivedItems = new List<ItemModel>();
            var possibleRewards = containerAsset.Rewards.OrderBy(x => Guid.NewGuid()).ToList();
            var rewardsToReceive = containerAsset.RewardAmount;
            var receivedRewardsAmount = 0;
            var error = false;

            ItemId = containerItem.ItemId;
            var needChance = rewardsToReceive < possibleRewards.Count;

            while (receivedRewardsAmount < rewardsToReceive && !error)
            {
                foreach (var possibleReward in possibleRewards)
                {
                    if (needChance && possibleReward.Chance < UtilitiesFunctions.RandomDouble())
                        continue;

                    var contentItem = new ItemModel();
                    contentItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == possibleReward.ItemId));

                    if (contentItem.ItemInfo == null)
                    {
                        client.Send(new SystemMessagePacket($"Invalid item info for item {possibleReward.ItemId}."));
                        _logger.Warning($"Invalid item info for item {possibleReward.ItemId} in tamer {client.TamerId} scan.");
                        error = true;
                        return;
                    }

                    contentItem.SetItemId(possibleReward.ItemId);
                    contentItem.SetAmount(UtilitiesFunctions.RandomInt(possibleReward.MinAmount, possibleReward.MaxAmount));

                    if (contentItem.IsTemporary)
                        contentItem.SetRemainingTime((uint)contentItem.ItemInfo.UsageTimeMinutes);

                    var tempItem = (ItemModel)contentItem.Clone();
                    receivedItems.Add(tempItem);
                    receivedRewardsAmount++;

                    if (receivedRewardsAmount >= rewardsToReceive || error)
                        break;
                }
            }

            if (error)
            {
                client.Send(new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo?.Type ?? 0).Serialize());
                return;
            }

            var receiveList = string.Join(',', receivedItems.Select(x => $"{x.ItemId} x{x.Amount}"));
            _logger.Verbose($"Character {client.TamerId} opened box {containerItem.ItemId} and obtained {receiveList}");

            // Verifica se há espaço suficiente no inventário
            var inventory = client.Tamer.Inventory;
            var availableSlots = inventory.Size - inventory.Items.Count(i => i.ItemId != 0);
            var requiredSlots = 0;

            foreach (var item in receivedItems)
            {
                var existingItem = inventory.Items.FirstOrDefault(i =>
                    i.ItemId == item.ItemId && i.ItemInfo?.Overlap > 1 && i.Amount < i.ItemInfo.Overlap);

                var needsSlot = existingItem == null ||
                    (existingItem != null && existingItem.Amount + item.Amount > existingItem.ItemInfo?.Overlap);

                if (needsSlot)
                    requiredSlots++;
            }

            if (availableSlots < requiredSlots)
            {
                client.Send(new SystemMessagePacket("Not enough space in inventory to open the box."));
                client.Send(new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo?.Type ?? 0).Serialize());
                return;
            }

            client.Tamer.Inventory.RemoveOrReduceItem(containerItem, 1, itemSlot);
            await _sender.Send(new UpdateItemCommand(containerItem));
            client.Send(new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize());

            foreach (var item in receivedItems)
            {
                var existingItem = inventory.Items.FirstOrDefault(i =>
                    i.ItemId == item.ItemId && i.Amount < i.ItemInfo?.Overlap);

                if (existingItem != null)
                {
                    existingItem.IncreaseAmount(item.Amount);

                    var tempItem = (ItemModel)item.Clone();
                    tempItem.SetSlot(existingItem.Slot);
                    client.Send(new ReceiveItemPacket(tempItem, InventoryTypeEnum.Inventory, existingItem.Slot));
                    await _sender.Send(new UpdateItemCommand(existingItem));
                }
                else
                {
                    var slot = inventory.InsertItem(item);
                    if (slot >= 0)
                    {
                        item.SetSlot(slot);
                        client.Send(new ReceiveItemPacket(item, InventoryTypeEnum.Inventory, slot));
                        await _sender.Send(new UpdateItemCommand(item));
                    }

                    if (containerAsset.Rewards.FirstOrDefault(x => x.ItemId == item.ItemId)?.Rare == true)
                    {
                        client.SendToAll(new NeonMessagePacket(NeonMessageTypeEnum.Item, client.Tamer.Name, containerItem.ItemId, item.ItemId).Serialize());
                    }
                }
            }

            // Buff especial para a box 70102
            if (ItemId == 70102 || ItemId == 70050)
            {
                var buffData = new List<(int BuffId, int Value1, int Value2)>
        {
            (50121, 2700022, 2592000),
            (50122, 2700023, 2592000),
            (50123, 2700024, 2592000)
        };

                foreach (var (BuffId, Value1, Value2) in buffData)
                {
                    var buff = _assets.BuffInfo.FirstOrDefault(x => x.SkillCode == Value1);
                    if (buff != null)
                    {
                        if (!client.Tamer.BuffList.Buffs.Any(x => x.BuffId == BuffId))
                        {
                            var duration = UtilitiesFunctions.RemainingTimeSeconds(Value2);
                            var newCharacterBuff = CharacterBuffModel.Create(BuffId, Value1, Value2);
                            newCharacterBuff.SetBuffInfo(buff);
                            client.Tamer.BuffList.Add(newCharacterBuff);
                            await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));

                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new AddBuffPacket(client.Tamer.GeneralHandler, buff, 0, duration).Serialize());
                        }
                        else
                        {
                            var BuffInfo = client.Tamer.BuffList.Buffs.FirstOrDefault(x => x.BuffId == BuffId);
                            if (BuffInfo != null)
                            {
                                BuffInfo.SetDuration(Value2);
                                var duration = UtilitiesFunctions.RemainingTimeSeconds(BuffInfo.Duration);

                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateBuffPacket(client.Tamer.GeneralHandler, buff, 0, duration).Serialize());

                                await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));
                            }
                        }
                    }
                }

                int time = 30 * 24 * 60 * 60;
                client.IncreaseMembershipDuration(time);

                var expiration = client.MembershipExpirationDate!.Value;
                var secondsLeft = (int)(expiration - DateTime.UtcNow).TotalSeconds;

                client.Send(new MembershipPacket(expiration, secondsLeft));
                await _sender.Send(new UpdateAccountMembershipCommand(client.AccountId, expiration));

                client.Send(new UpdateStatusPacket(client.Tamer));
            }
        }

        private async Task BuffItem(GameClient client, short itemSlot, ItemModel targetItem)
        {
            var buff = _assets.BuffInfo.FirstOrDefault(x => x.SkillCode == targetItem.ItemInfo.SkillCode);
            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
            if (buff != null)
            {
                var duration = UtilitiesFunctions.RemainingTimeSeconds(targetItem.ItemInfo.TimeInSeconds);

                var newCharacterBuff = CharacterBuffModel.Create(buff.BuffId, buff.SkillId, targetItem.ItemInfo.TypeN,
                    targetItem.ItemInfo.TimeInSeconds);
                newCharacterBuff.SetBuffInfo(buff);

                var newDigimonBuff = DigimonBuffModel.Create(buff.BuffId, buff.SkillId, targetItem.ItemInfo.TypeN,
                    targetItem.ItemInfo.TimeInSeconds);

                newDigimonBuff.SetBuffInfo(buff);

                var characterBuffs = new List<SkillCodeApplyAttributeEnum>
               {
                   SkillCodeApplyAttributeEnum.MS,
                   SkillCodeApplyAttributeEnum.MovementSpeedIncrease,
                   SkillCodeApplyAttributeEnum.EXP,
                   SkillCodeApplyAttributeEnum.AttributeExperienceAdded
               };

                if (characterBuffs.Contains(buff.SkillInfo.Apply.First().Attribute))
                {
                    if (client.Tamer.BuffList.ActiveBuffs.Any(x => x.BuffId == buff.BuffId))
                    {
                        client.Tamer.BuffList.ForceExpired(newCharacterBuff.BuffId);
                        client.Tamer.BuffList.Add(newCharacterBuff);
                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new RemoveBuffPacket(client.Tamer.GeneralHandler, newCharacterBuff.BuffId)
                                        .Serialize());

                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Tamer.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new RemoveBuffPacket(client.Tamer.GeneralHandler, newCharacterBuff.BuffId)
                                        .Serialize());

                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Tamer.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new RemoveBuffPacket(client.Tamer.GeneralHandler, newCharacterBuff.BuffId)
                                        .Serialize());

                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Tamer.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new RemoveBuffPacket(client.Tamer.GeneralHandler, newCharacterBuff.BuffId)
                                        .Serialize());

                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Tamer.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;
                        }
                    }
                    else
                    {
                        client.Tamer.BuffList.Add(newCharacterBuff);
                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Tamer.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Tamer.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Tamer.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Tamer.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;
                        }
                    }
                }
                else
                {
                    if (client.Partner.BuffList.ActiveBuffs.Any(x => x.BuffId == buff.BuffId))
                    {
                        client.Partner.BuffList.ForceExpired(newDigimonBuff.BuffId);
                        client.Partner.BuffList.Add(newDigimonBuff);
                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new RemoveBuffPacket(client.Partner.GeneralHandler, newCharacterBuff.BuffId)
                                        .Serialize());


                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Partner.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());

                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new RemoveBuffPacket(client.Partner.GeneralHandler, newCharacterBuff.BuffId)
                                        .Serialize());


                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Partner.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());

                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new RemoveBuffPacket(client.Partner.GeneralHandler, newCharacterBuff.BuffId)
                                        .Serialize());


                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Partner.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;

                            default:
                                client.Partner.BuffList.ForceExpired(newDigimonBuff.BuffId);
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new RemoveBuffPacket(client.Partner.GeneralHandler, newCharacterBuff.BuffId)
                                        .Serialize());

                                client.Partner.BuffList.Add(newDigimonBuff);
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Partner.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;
                        }
                    }
                    else
                    {
                        client.Partner.BuffList.Add(newDigimonBuff);
                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Partner.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Partner.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Partner.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new AddBuffPacket(client.Partner.GeneralHandler, buff,
                                        (short)targetItem.ItemInfo.TypeN,
                                        duration).Serialize());

                                break;
                        }
                    }
                }


                client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1);
                await _sender.Send(new UpdateItemCommand(targetItem));
                await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));
                await _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));

                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                        new UpdateStatusPacket(client.Tamer).Serialize()
                    )
                );
            }
            else
            {
                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                    )
                );
            }
        }

        private async Task ConsumeExpItem(GameClient client, short itemSlot, ItemModel targetItem)
        {
            if (targetItem.ItemInfo?.SkillInfo == null)
            {
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemConsumeFailPacket(itemSlot, targetItem.ItemInfo.Type).Serialize(),
                    new SystemMessagePacket($"Invalid skill info for item id {targetItem.ItemId}.").Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                ));

                _logger.Error($"Invalid skill info for item id {targetItem.ItemId} and tamer id {client.TamerId}.");
                return;
            }

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
            foreach (var apply in targetItem.ItemInfo?.SkillInfo.Apply)
            {
                //_logger.Information($"ApplyType: {apply.Type}");

                switch (apply.Type)
                {
                    case SkillCodeApplyTypeEnum.None:
                        break;

                    case SkillCodeApplyTypeEnum.Default:
                        {
                            switch (apply.Attribute)
                            {
                                case SkillCodeApplyAttributeEnum.EXP:
                                    {
                                        switch (targetItem.ItemInfo.Target)
                                        {
                                            case ItemConsumeTargetEnum.Both:
                                                {
                                                    Random random = new Random();
                                                    int randomValue = random.Next(targetItem.ItemInfo.ApplyValueMin,
                                                        targetItem.ItemInfo.ApplyValueMax + 1);

                                                    int value = apply.Value * (randomValue / 100);

                                                    var result = _expManager.ReceiveTamerExperience(value, client.Tamer);
                                                    var result2 = _expManager.ReceiveDigimonExperience(value, client.Tamer.Partner);

                                                    if (result.Success)
                                                    {
                                                        client.Send(
                                                            new ReceiveExpPacket(
                                                                value,
                                                                0,
                                                                client.Tamer.CurrentExperience,
                                                                client.Tamer.Partner.GeneralHandler,
                                                                0,
                                                                0,
                                                                client.Tamer.Partner.CurrentExperience,
                                                                0
                                                            )
                                                        );
                                                    }
                                                    else
                                                    {
                                                        client.Send(new SystemMessagePacket(
                                                            $"No proper configuration for tamer {client.Tamer.Model} leveling."));
                                                        return;
                                                    }

                                                    if (result.LevelGain > 0)
                                                    {
                                                        client.Tamer.SetLevelStatus(
                                                            _statusManager.GetTamerLevelStatus(
                                                                client.Tamer.Model,
                                                                client.Tamer.Level
                                                            )
                                                        );

                                                        switch (mapConfig?.Type)
                                                        {
                                                            case MapTypeEnum.Dungeon:
                                                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.GeneralHandler,
                                                                        client.Tamer.Level).Serialize());
                                                                break;

                                                            case MapTypeEnum.Event:
                                                                _eventServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.GeneralHandler,
                                                                        client.Tamer.Level).Serialize());
                                                                break;

                                                            case MapTypeEnum.Pvp:
                                                                _pvpServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.GeneralHandler,
                                                                        client.Tamer.Level).Serialize());
                                                                break;

                                                            default:
                                                                _mapServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.GeneralHandler,
                                                                        client.Tamer.Level).Serialize());
                                                                break;
                                                        }

                                                        client.Tamer.FullHeal();

                                                        client.Send(new UpdateStatusPacket(client.Tamer));
                                                    }

                                                    if (result.Success)
                                                        await _sender.Send(new UpdateCharacterExperienceCommand(client.TamerId,
                                                            client.Tamer.CurrentExperience, client.Tamer.Level));

                                                    if (result2.Success)
                                                    {
                                                        client.Send(
                                                            new ReceiveExpPacket(
                                                                0,
                                                                0,
                                                                client.Tamer.CurrentExperience,
                                                                client.Tamer.Partner.GeneralHandler,
                                                                value,
                                                                0,
                                                                client.Tamer.Partner.CurrentExperience,
                                                                0
                                                            )
                                                        );
                                                    }

                                                    if (result2.LevelGain > 0)
                                                    {
                                                        client.Partner.SetBaseStatus(
                                                            _statusManager.GetDigimonBaseStatus(
                                                                client.Partner.CurrentType,
                                                                client.Partner.Level,
                                                                client.Partner.Size
                                                            )
                                                        );

                                                        switch (mapConfig?.Type)
                                                        {
                                                            case MapTypeEnum.Dungeon:
                                                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level
                                                                    ).Serialize()
                                                                );

                                                                break;

                                                            case MapTypeEnum.Event:
                                                                _eventServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level
                                                                    ).Serialize()
                                                                );
                                                                break;

                                                            case MapTypeEnum.Pvp:
                                                                _pvpServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level
                                                                    ).Serialize()
                                                                );
                                                                break;

                                                            default:
                                                                _mapServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level
                                                                    ).Serialize()
                                                                );
                                                                break;
                                                        }

                                                        client.Partner.FullHeal();

                                                        client.Send(new UpdateStatusPacket(client.Tamer));
                                                    }

                                                    if (result2.Success)
                                                        await _sender.Send(new UpdateDigimonExperienceCommand(client.Partner));
                                                }
                                                break;

                                            case ItemConsumeTargetEnum.Digimon:
                                                {
                                                    Random random = new Random();
                                                    int randomValue = random.Next(targetItem.ItemInfo.ApplyValueMin,
                                                        targetItem.ItemInfo.ApplyValueMax + 1);

                                                    int value = apply.Value * (randomValue / 100);

                                                    var digimonResult =
                                                        _expManager.ReceiveDigimonExperience(value, client.Tamer.Partner);

                                                    if (digimonResult.Success)
                                                    {
                                                        client.Send(
                                                            new ReceiveExpPacket(
                                                                0,
                                                                0,
                                                                client.Tamer.CurrentExperience,
                                                                client.Tamer.Partner.GeneralHandler,
                                                                value,
                                                                0,
                                                                client.Tamer.Partner.CurrentExperience,
                                                                0
                                                            )
                                                        );
                                                    }

                                                    if (digimonResult.LevelGain > 0)
                                                    {
                                                        client.Partner.SetBaseStatus(
                                                            _statusManager.GetDigimonBaseStatus(
                                                                client.Partner.CurrentType,
                                                                client.Partner.Level,
                                                                client.Partner.Size
                                                            )
                                                        );

                                                        switch (mapConfig?.Type)
                                                        {
                                                            case MapTypeEnum.Dungeon:
                                                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level
                                                                    ).Serialize()
                                                                );
                                                                break;

                                                            case MapTypeEnum.Event:
                                                                _eventServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level
                                                                    ).Serialize()
                                                                );
                                                                break;

                                                            case MapTypeEnum.Pvp:
                                                                _pvpServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level
                                                                    ).Serialize()
                                                                );
                                                                break;

                                                            default:
                                                                _mapServer.BroadcastForTamerViewsAndSelf(
                                                                    client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.Partner.GeneralHandler,
                                                                        client.Tamer.Partner.Level
                                                                    ).Serialize()
                                                                );
                                                                break;
                                                        }

                                                        client.Partner.FullHeal();

                                                        client.Send(new UpdateStatusPacket(client.Tamer));
                                                    }

                                                    if (digimonResult.Success)
                                                        await _sender.Send(new UpdateDigimonExperienceCommand(client.Partner));
                                                }
                                                break;

                                            case ItemConsumeTargetEnum.Tamer:
                                                {
                                                    Random random = new Random();
                                                    int randomValue = random.Next(targetItem.ItemInfo.ApplyValueMin,
                                                        targetItem.ItemInfo.ApplyValueMax + 1);

                                                    int value = apply.Value * (randomValue / 100);

                                                    var result = _expManager.ReceiveTamerExperience(value, client.Tamer);

                                                    if (result.Success)
                                                    {
                                                        client.Send(
                                                            new ReceiveExpPacket(
                                                                value,
                                                                0,
                                                                client.Tamer.CurrentExperience,
                                                                client.Tamer.Partner.GeneralHandler,
                                                                0,
                                                                0,
                                                                client.Tamer.Partner.CurrentExperience,
                                                                0
                                                            )
                                                        );
                                                    }
                                                    else
                                                    {
                                                        client.Send(new SystemMessagePacket(
                                                            $"No proper configuration for tamer {client.Tamer.Model} leveling."));
                                                        return;
                                                    }

                                                    if (result.LevelGain > 0)
                                                    {
                                                        client.Tamer.SetLevelStatus(
                                                            _statusManager.GetTamerLevelStatus(
                                                                client.Tamer.Model,
                                                                client.Tamer.Level
                                                            )
                                                        );

                                                        switch (mapConfig?.Type)
                                                        {
                                                            case MapTypeEnum.Dungeon:
                                                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.GeneralHandler,
                                                                        client.Tamer.Level).Serialize());
                                                                break;

                                                            case MapTypeEnum.Event:
                                                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.GeneralHandler,
                                                                        client.Tamer.Level).Serialize());
                                                                break;

                                                            case MapTypeEnum.Pvp:
                                                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.GeneralHandler,
                                                                        client.Tamer.Level).Serialize());
                                                                break;

                                                            default:
                                                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                                                    new LevelUpPacket(
                                                                        client.Tamer.GeneralHandler,
                                                                        client.Tamer.Level).Serialize());
                                                                break;
                                                        }


                                                        client.Tamer.FullHeal();

                                                        client.Send(new UpdateStatusPacket(client.Tamer));
                                                    }

                                                    if (result.Success)
                                                        await _sender.Send(new UpdateCharacterExperienceCommand(client.TamerId,
                                                            client.Tamer.CurrentExperience, client.Tamer.Level));
                                                }
                                                break;
                                        }
                                    }
                                    break;

                                default:
                                    {
                                        _logger.Error(
                                            $"ApplyAttribute: {apply.Attribute} not configured!! (ItemConsumePacket)");
                                    }
                                    break;
                            }
                        }
                        break;

                    default:
                        {
                            _logger.Error($"ApplyType: {apply.Type} not configured! (ItemConsumePacket)");
                            return;
                        }
                }
            }

            client.Tamer.Inventory.RemoveOrReduceItem(targetItem, 1, itemSlot);

            await _sender.Send(new UpdateItemCommand(targetItem));
            await _sender.Send(new UpdateCharacterBasicInfoCommand(client.Tamer));
            client.blockAchievement = false;

            client.Send(UtilitiesFunctions.GroupPackets(
                new ItemConsumeSuccessPacket(client.Tamer.GeneralHandler, itemSlot).Serialize(),
                new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));

            // _logger.Verbose($"Tamer {client.Tamer.Name} consumed 1 : {targetItem.ItemInfo.Name}");
        }
    }
}