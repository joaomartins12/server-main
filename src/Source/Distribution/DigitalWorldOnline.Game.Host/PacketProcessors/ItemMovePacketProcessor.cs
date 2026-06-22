using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Account;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;


namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ItemMovePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.MoveItem;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;

        public ItemMovePacketProcessor(
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            EventServer eventServer,
            PvpServer pvpServer,
            ISender sender,
            ILogger logger,
            IMapper mapper)
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _sender = sender;
            _logger = logger;
            _mapper = mapper;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var originSlot = packet.ReadShort();
            var destinationSlot = packet.ReadShort();

            var itemListMovimentation = UtilitiesFunctions.SwitchItemList(originSlot, destinationSlot);

            var success = await SwapItems(client, originSlot, destinationSlot, itemListMovimentation);

            //_logger.Information($"Item Moved Packet !!");

            if (success)
            {
                switch (itemListMovimentation)
                {
                    case ItemListMovimentationEnum.InventoryToInventory:
                        {
                            client.Tamer.Inventory.CheckEmptyItems();
                            var item = client.Tamer.Inventory.FindItemBySlot(originSlot);
                            var item2 = client.Tamer.Inventory.FindItemBySlot(destinationSlot);
                            await _sender.Send(new UpdateItemCommand(item));
                            await _sender.Send(new UpdateItemCommand(item2));
                        }
                        break;

                    case ItemListMovimentationEnum.InventoryToEquipment:
                        {
                            var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                            var dstSlot = destinationSlot == GeneralSizeEnum.XaiSlot.GetHashCode()
                                ? 11
                                : destinationSlot - GeneralSizeEnum.EquipmentMinSlot.GetHashCode();

                            await _sender.Send(new UpdateItemCommand(client.Tamer.Inventory.FindItemBySlot(srcSlot)));
                            await _sender.Send(new UpdateItemCommand(client.Tamer.Equipment.FindItemBySlot(dstSlot)));
                        }
                        break;

                    case ItemListMovimentationEnum.EquipmentToInventory:
                        {
                            var srcSlot = originSlot == GeneralSizeEnum.XaiSlot.GetHashCode()
                                ? 11
                                : originSlot - GeneralSizeEnum.EquipmentMinSlot.GetHashCode();
                            var dstSlot = destinationSlot; // inventário é direto

                            await _sender.Send(new UpdateItemCommand(client.Tamer.Equipment.FindItemBySlot(srcSlot)));
                            await _sender.Send(new UpdateItemCommand(client.Tamer.Inventory.FindItemBySlot(dstSlot)));
                        }
                        break;





                    case ItemListMovimentationEnum.InventoryToDigivice:
                        {
                            var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                            var dstSlot = destinationSlot - GeneralSizeEnum.DigiviceSlot.GetHashCode();

                            await _sender.Send(new UpdateItemCommand(client.Tamer.Inventory.FindItemBySlot(srcSlot)));
                            await _sender.Send(new UpdateItemCommand(client.Tamer.Digivice.FindItemBySlot(dstSlot)));
                        }
                        break;

                    case ItemListMovimentationEnum.DigiviceToInventory:
                        {
                            var srcSlot = 0;
                            var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                            await _sender.Send(new UpdateItemCommand(client.Tamer.Digivice.FindItemBySlot(srcSlot)));
                            await _sender.Send(new UpdateItemCommand(client.Tamer.Inventory.FindItemBySlot(dstSlot)));
                        }
                        break;


                    case ItemListMovimentationEnum.InventoryToChipset:
                        {
                            var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                            var dstSlot = destinationSlot - GeneralSizeEnum.ChipsetMinSlot.GetHashCode();

                            var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                            var destItem = client.Tamer.ChipSets.FindItemBySlot(dstSlot);

                            if (sourceItem != null)
                                await _sender.Send(new UpdateItemCommand(sourceItem));
                            if (destItem != null)
                                await _sender.Send(new UpdateItemCommand(destItem));
                        }
                        break;

                    case ItemListMovimentationEnum.ChipsetToInventory:
                        {
                            var srcSlot = originSlot - GeneralSizeEnum.ChipsetMinSlot.GetHashCode();
                            var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                            var sourceItem = client.Tamer.ChipSets.FindItemBySlot(srcSlot);
                            var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                            if (sourceItem != null)
                                await _sender.Send(new UpdateItemCommand(sourceItem));
                            if (destItem != null)
                                await _sender.Send(new UpdateItemCommand(destItem));
                        }
                        break;


                    case ItemListMovimentationEnum.WarehouseToInventory:
                        {
                            client.Tamer.Inventory.CheckEmptyItems();
                            client.Tamer.Warehouse.CheckEmptyItems();
                        }
                        break;

                    case ItemListMovimentationEnum.InventoryToWarehouse:
                        {
                            client.Tamer.Inventory.CheckEmptyItems();
                            client.Tamer.Warehouse.CheckEmptyItems();
                        }
                        break;


                    case ItemListMovimentationEnum.AccountWarehouseToInventory:
                        {
                            client.Tamer.AccountWarehouse.CheckEmptyItems();
                            client.Tamer.Inventory.CheckEmptyItems();

                            await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                        }
                        break;
                    case ItemListMovimentationEnum.InventoryToAccountWarehouse:
                        {
                            client.Tamer.Inventory.CheckEmptyItems();
                            client.Tamer.AccountWarehouse.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                        }
                        break;

                    case ItemListMovimentationEnum.AccountWarehouseToWarehouse:
                        {
                            client.Tamer.AccountWarehouse.CheckEmptyItems();
                            client.Tamer.Warehouse.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                        }
                        break;
                    case ItemListMovimentationEnum.WarehouseToAccountWarehouse:
                        {
                            client.Tamer.AccountWarehouse.CheckEmptyItems();
                            client.Tamer.Warehouse.CheckEmptyItems();
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.Warehouse));
                            await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountWarehouse));
                        }
                        break;

                    case ItemListMovimentationEnum.AccountWarehouseToAccountWarehouse:
                        {
                            client.Tamer.AccountWarehouse.CheckEmptyItems();
                        }
                        break;


                    case ItemListMovimentationEnum.WarehouseToWarehouse:
                        {
                            // Só precisa garantir que os slots vazios sejam limpos
                            client.Tamer.Warehouse.CheckEmptyItems();
                        }
                        break;




                }

                client.Send(
                    UtilitiesFunctions.GroupPackets(
                        new ItemMoveSuccessPacket(originSlot, destinationSlot).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()
                    )
                );


                if (originSlot == GeneralSizeEnum.XaiSlot.GetHashCode())
                {
                    client.Tamer.Xai.RemoveXai();
                    client.Send(new XaiInfoPacket());
                    client.Send(new TamerXaiResourcesPacket(0, (short)client.Tamer.XGauge));
                    await _sender.Send(new UpdateCharacterXaiCommand(client.Tamer.Xai));
                }

                if (destinationSlot == GeneralSizeEnum.XaiSlot.GetHashCode())
                {
                    var ItemId = client.Tamer.Equipment.FindItemBySlot(destinationSlot - 1000).ItemId;

                    var XaiInfo = _mapper.Map<XaiAssetModel>(await _sender.Send(new XaiInformationQuery(ItemId)));

                    client.Tamer.Xai.EquipXai(XaiInfo.ItemId, XaiInfo.XGauge, XaiInfo.XCrystals);

                    client.Send(new XaiInfoPacket(client.Tamer.Xai));
                    client.Send(new TamerXaiResourcesPacket(client.Tamer.XGauge, client.Tamer.XCrystals));

                    await _sender.Send(new UpdateCharacterXaiCommand(client.Tamer.Xai));
                }

            }
            else
            {
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemMoveFailPacket(originSlot, destinationSlot).Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));

                _logger.Warning(
                    $"Character {client.TamerId} failled to move item from {originSlot} to {destinationSlot}.");
            }
        }

        private async Task<bool> SwapItems(GameClient client, short originSlot, short destinationSlot,
            ItemListMovimentationEnum itemListMovimentation)
        {
            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            switch (itemListMovimentation)
            {
                case ItemListMovimentationEnum.InventoryToInventory:
                    return client.Tamer.Inventory.MoveItem(originSlot, destinationSlot);

                case ItemListMovimentationEnum.InventoryToDigivice:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.DigiviceSlot.GetHashCode();

                        var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Digivice.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            client.Tamer.Digivice.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.Inventory.AddItemWithSlot(tempItem, srcSlot);
                        }
                        else
                        {
                            client.Tamer.Digivice.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId(); // limpa slot do inventário
                            client.Tamer.Inventory.AddItemWithSlot(new ItemModel(), srcSlot);
                        }

                        await _sender.Send(new UpdateItemCommand(client.Tamer.Inventory.FindItemBySlot(srcSlot)));
                        await _sender.Send(new UpdateItemCommand(client.Tamer.Digivice.FindItemBySlot(dstSlot)));

                        var appearanceItem = destItem.ItemId > 0 ? destItem : new ItemModel();
                        var appearanceAmount = destItem.ItemId > 0 ? (byte)1 : (byte)0;

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, appearanceItem, appearanceAmount).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, appearanceItem, appearanceAmount).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, appearanceItem, appearanceAmount).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, appearanceItem, appearanceAmount).Serialize());
                                break;
                        }

                        client.Send(new UpdateStatusPacket(client.Tamer));

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                        }

                        return true;
                    }


                case ItemListMovimentationEnum.ChipsetToInventory:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.ChipsetMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.ChipSets.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.ChipSets.AddItemWithSlot(tempItem, srcSlot);
                        }
                        else
                        {
                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId();
                            client.Tamer.ChipSets.AddItemWithSlot(new ItemModel(), srcSlot);
                        }

                        await _sender.Send(new UpdateItemCommand(client.Tamer.Inventory.FindItemBySlot(dstSlot)));
                        await _sender.Send(new UpdateItemCommand(client.Tamer.ChipSets.FindItemBySlot(srcSlot)));

                        var appearanceItem = destItem.ItemId > 0 ? destItem : new ItemModel();
                        var appearanceAmount = destItem.ItemId > 0 ? (byte)1 : (byte)0;

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, appearanceItem, appearanceAmount).Serialize());
                                break;
                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, appearanceItem, appearanceAmount).Serialize());
                                break;
                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, appearanceItem, appearanceAmount).Serialize());
                                break;
                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot, appearanceItem, appearanceAmount).Serialize());
                                break;
                        }

                        client.Send(new UpdateStatusPacket(client.Tamer));

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                        }

                        return true;
                    }


                case ItemListMovimentationEnum.InventoryToChipset:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.ChipsetMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.ChipSets.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            client.Tamer.ChipSets.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.Inventory.AddItemWithSlot(tempItem, srcSlot);
                        }
                        else
                        {
                            client.Tamer.ChipSets.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId();
                            client.Tamer.Inventory.AddItemWithSlot(new ItemModel(), srcSlot);
                        }

                        await _sender.Send(new UpdateItemCommand(client.Tamer.Inventory.FindItemBySlot(srcSlot)));
                        await _sender.Send(new UpdateItemCommand(client.Tamer.ChipSets.FindItemBySlot(dstSlot)));

                        var appearanceItem = destItem.ItemId > 0 ? destItem : new ItemModel();
                        var appearanceAmount = destItem.ItemId > 0 ? (byte)1 : (byte)0;

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, appearanceItem, appearanceAmount).Serialize());
                                break;
                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, appearanceItem, appearanceAmount).Serialize());
                                break;
                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, appearanceItem, appearanceAmount).Serialize());
                                break;
                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot, appearanceItem, appearanceAmount).Serialize());
                                break;
                        }

                        client.Send(new UpdateStatusPacket(client.Tamer));

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                        }

                        return true;
                    }

                case ItemListMovimentationEnum.InventoryToEquipment:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                        var dstSlot = destinationSlot == GeneralSizeEnum.XaiSlot.GetHashCode()
                            ? 11
                            : destinationSlot - GeneralSizeEnum.EquipmentMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Equipment.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            client.Tamer.Equipment.AddItemWithSlot(sourceItem, dstSlot);

                            client.Tamer.Inventory.AddItemWithSlot(tempItem, srcSlot);

                            await _sender.Send(new UpdateItemCommand(client.Tamer.Inventory.FindItemBySlot(srcSlot)));
                            await _sender.Send(new UpdateItemCommand(client.Tamer.Equipment.FindItemBySlot(dstSlot)));

                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;

                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;

                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;

                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;
                            }
                        }
                        else
                        {
                            client.Tamer.Equipment.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId();

                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;

                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;

                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;

                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(
                                        client.TamerId,
                                        new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)dstSlot,
                                            destItem,
                                            1).Serialize());
                                    break;
                            }
                        }

                        client.Send(new UpdateStatusPacket(client.Tamer));

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                        }

                        //if (client.Tamer.HasXai)
                        //{
                        //    var xai = await _sender.Send(new XaiInformationQuery(client.Tamer.Xai?.ItemId ?? 0));
                        //    client.Tamer.SetXai(_mapper.Map<CharacterXaiModel>(xai));
                        //
                        //    client.Send(new XaiInfoPacket(client.Tamer.Xai));
                        //
                        //    client.Send(new TamerXaiResourcesPacket(client.Tamer.XGauge, client.Tamer.XCrystals));
                        //}

                        return true;
                    }




                case ItemListMovimentationEnum.InventoryToWarehouse:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();

                        // Obter os itens envolvidos
                        var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Warehouse.FindItemBySlot(dstSlot);

                        // Verificar se o item de origem existe
                        if (sourceItem == null)
                            return false;

                        // Bloqueio para evitar condições de corrida
                        lock (client.Tamer.Inventory)
                            lock (client.Tamer.Warehouse)
                            {
                                if (destItem != null && destItem.ItemId > 0)
                                {
                                    var tempItem = (ItemModel)destItem.Clone();
                                    tempItem.SetItemInfo(destItem.ItemInfo);

                                    if (destItem.ItemId == sourceItem.ItemId)
                                    {
                                        // Validar se a soma não excede o limite de sobreposição
                                        var maxOverlap = destItem.ItemInfo.Overlap;
                                        if (destItem.Amount + sourceItem.Amount > maxOverlap)
                                        {
                                            var remainingSpace = maxOverlap - destItem.Amount;
                                            destItem.IncreaseAmount(remainingSpace);
                                            sourceItem.ReduceAmount(remainingSpace);
                                        }
                                        else
                                        {
                                            destItem.IncreaseAmount(sourceItem.Amount);
                                            sourceItem.ReduceAmount(sourceItem.Amount);
                                        }
                                    }
                                    else
                                    {
                                        client.Tamer.Warehouse.AddItemWithSlot(sourceItem, dstSlot);
                                        client.Tamer.Inventory.AddItemWithSlot(tempItem, srcSlot);
                                    }
                                }
                                else
                                {
                                    client.Tamer.Warehouse.AddItemWithSlot(sourceItem, dstSlot);
                                    sourceItem.SetItemId(); // Limpa o slot do inventário
                                }
                            }

                        // Persistir apenas os itens afetados
                        var updatedInventoryItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var updatedWarehouseItem = client.Tamer.Warehouse.FindItemBySlot(dstSlot);

                        if (updatedInventoryItem != null)
                            await _sender.Send(new UpdateItemCommand(updatedInventoryItem));
                        if (updatedWarehouseItem != null)
                            await _sender.Send(new UpdateItemCommand(updatedWarehouseItem));

                        return true;
                    }


                case ItemListMovimentationEnum.InventoryToAccountWarehouse:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.AccountWarehouse.FindItemBySlot(dstSlot);

                        if (sourceItem == null)
                            return false;

                        // Verifica se o item é "Bound" (não transferível)
                        if (sourceItem.ItemInfo.BoundType == 2)
                        {
                            // await _mapServer.CallDiscord(
                            // $"{client.Tamer.Name} tentou movimentar um item não-transferível (Bound) e foi desconectado.",
                            // client,
                            //  "7981ff",
                            // "ITEM MOVEMENT VIOLATION",
                            //  "1352604335162265670",
                            // true
                            // );

                            client.Disconnect();
                            return false;
                        }

                        // Protege contra concorrência
                        lock (client.Tamer.Inventory)
                            lock (client.Tamer.AccountWarehouse)
                            {
                                if (destItem != null && destItem.ItemId > 0)
                                {
                                    var tempItem = (ItemModel)destItem.Clone();
                                    tempItem.SetItemInfo(destItem.ItemInfo);

                                    if (destItem.ItemId == sourceItem.ItemId)
                                    {
                                        var maxOverlap = destItem.ItemInfo.Overlap;
                                        if (destItem.Amount + sourceItem.Amount > maxOverlap)
                                        {
                                            var remainingSpace = maxOverlap - destItem.Amount;
                                            destItem.IncreaseAmount(remainingSpace);
                                            sourceItem.ReduceAmount(remainingSpace);
                                        }
                                        else
                                        {
                                            destItem.IncreaseAmount(sourceItem.Amount);
                                            sourceItem.ReduceAmount(sourceItem.Amount);
                                        }
                                    }
                                    else
                                    {
                                        client.Tamer.AccountWarehouse.AddItemWithSlot(sourceItem, dstSlot);
                                        client.Tamer.Inventory.AddItemWithSlot(tempItem, srcSlot);
                                    }
                                }
                                else
                                {
                                    client.Tamer.AccountWarehouse.AddItemWithSlot(sourceItem, dstSlot);
                                    sourceItem.SetItemId();
                                }
                            }

                        // Persistência dos slots afetados
                        var updatedInventoryItem = client.Tamer.Inventory.FindItemBySlot(srcSlot);
                        var updatedWarehouseItem = client.Tamer.AccountWarehouse.FindItemBySlot(dstSlot);

                        if (updatedInventoryItem != null)
                            await _sender.Send(new UpdateItemCommand(updatedInventoryItem));
                        if (updatedWarehouseItem != null)
                            await _sender.Send(new UpdateItemCommand(updatedWarehouseItem));

                        // Log no Discord
                        var itemDetails = $"**{sourceItem.ItemInfo.Name}** x{sourceItem.Amount} movimentado para o armazém de conta.";
                        await _mapServer.CallDiscord(
                            $"**Movimentação de Item**\n" +
                            $"**Player**: {client.Tamer.Name} movimentou o item {itemDetails}",
                            client,
                            "00ff00",
                            "ITEM MOVEMENT",
                            "1374552350408315030"
                        );

                        return true;
                    }



                case ItemListMovimentationEnum.EquipmentToInventory:
                    {
                        var srcSlot = originSlot == GeneralSizeEnum.XaiSlot.GetHashCode()
                            ? 11
                            : originSlot - GeneralSizeEnum.EquipmentMinSlot.GetHashCode();
                        var dstSlot = destinationSlot;

                        var sourceItem = client.Tamer.Equipment.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.Equipment.AddItemWithSlot(tempItem, srcSlot);
                        }
                        else
                        {
                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId();

                            // Slot agora vazio no equipamento
                            client.Tamer.Equipment.AddItemWithSlot(new ItemModel(), srcSlot);
                        }

                        // Atualiza apenas os slots afetados
                        await _sender.Send(new UpdateItemCommand(client.Tamer.Inventory.FindItemBySlot(dstSlot)));
                        await _sender.Send(new UpdateItemCommand(client.Tamer.Equipment.FindItemBySlot(srcSlot)));

                        // Aparência
                        var appearanceItem = destItem.ItemId > 0 ? destItem : new ItemModel();
                        var appearanceAmount = destItem.ItemId > 0 ? (byte)1 : (byte)0;

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot,
                                        appearanceItem, appearanceAmount).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot,
                                        appearanceItem, appearanceAmount).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot,
                                        appearanceItem, appearanceAmount).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(
                                    client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, (byte)srcSlot,
                                        appearanceItem, appearanceAmount).Serialize());
                                break;
                        }

                        client.Send(new UpdateStatusPacket(client.Tamer));

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                        }

                        return true;
                    }



                case ItemListMovimentationEnum.DigiviceToInventory:
                    {
                        var srcSlot = 0;
                        var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Digivice.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.Digivice.AddItemWithSlot(tempItem, srcSlot);
                        }
                        else
                        {
                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId(); // limpa o slot do Digivice
                            client.Tamer.Digivice.AddItemWithSlot(new ItemModel(), srcSlot);
                        }

                        await _sender.Send(new UpdateItemCommand(client.Tamer.Inventory.FindItemBySlot(dstSlot)));
                        await _sender.Send(new UpdateItemCommand(client.Tamer.Digivice.FindItemBySlot(srcSlot)));

                        // Aparência (remover item do Digivice = quantidade 0)
                        var appearanceItem = destItem.ItemId > 0 ? destItem : new ItemModel();
                        var appearanceAmount = destItem.ItemId > 0 ? (byte)1 : (byte)0;

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, appearanceItem, appearanceAmount).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, appearanceItem, appearanceAmount).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, appearanceItem, appearanceAmount).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                    new UpdateTamerAppearancePacket(client.Tamer.AppearenceHandler, 13, appearanceItem, appearanceAmount).Serialize());
                                break;
                        }

                        client.Send(new UpdateStatusPacket(client.Tamer));

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;

                            default:
                                _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                                break;
                        }

                        return true;
                    }


                case ItemListMovimentationEnum.WarehouseToWarehouse:
                    {
                        var orgSlot = (short)(originSlot - (short)GeneralSizeEnum.WarehouseMinSlot);
                        var destSlot = (short)(destinationSlot - (short)GeneralSizeEnum.WarehouseMinSlot);

                        var success = client.Tamer.Warehouse.MoveItem(orgSlot, destSlot);

                        if (success)
                        {
                            var item1 = client.Tamer.Warehouse.FindItemBySlot(orgSlot);
                            var item2 = client.Tamer.Warehouse.FindItemBySlot(destSlot);

                            if (item1 != null)
                                await _sender.Send(new UpdateItemCommand(item1));
                            if (item2 != null)
                                await _sender.Send(new UpdateItemCommand(item2));
                        }

                        return success;
                    }



                case ItemListMovimentationEnum.AccountWarehouseToAccountWarehouse:
                    {
                        var orgSlot = (short)(originSlot - (short)GeneralSizeEnum.AccountWarehouseMinSlot);
                        var destSlot = (short)(destinationSlot - (short)GeneralSizeEnum.AccountWarehouseMinSlot);

                        var success = client.Tamer.AccountWarehouse.MoveItem(orgSlot, destSlot);

                        if (success)
                        {
                            var item1 = client.Tamer.AccountWarehouse.FindItemBySlot(orgSlot);
                            var item2 = client.Tamer.AccountWarehouse.FindItemBySlot(destSlot);

                            if (item1 != null)
                                await _sender.Send(new UpdateItemCommand(item1));
                            if (item2 != null)
                                await _sender.Send(new UpdateItemCommand(item2));
                        }

                        return success;
                    }


                case ItemListMovimentationEnum.WarehouseToInventory:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Warehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                        if (sourceItem == null)
                            return false;

                        lock (client.Tamer.Warehouse)
                            lock (client.Tamer.Inventory)
                            {
                                if (destItem != null && destItem.ItemId > 0)
                                {
                                    var tempItem = (ItemModel)destItem.Clone();
                                    tempItem.SetItemInfo(destItem.ItemInfo);

                                    if (destItem.ItemId == sourceItem.ItemId)
                                    {
                                        var maxOverlap = destItem.ItemInfo.Overlap;
                                        if (destItem.Amount + sourceItem.Amount > maxOverlap)
                                        {
                                            var remainingSpace = maxOverlap - destItem.Amount;
                                            destItem.IncreaseAmount(remainingSpace);
                                            sourceItem.ReduceAmount(remainingSpace);
                                        }
                                        else
                                        {
                                            destItem.IncreaseAmount(sourceItem.Amount);
                                            sourceItem.ReduceAmount(sourceItem.Amount);
                                        }
                                    }
                                    else
                                    {
                                        client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                                        client.Tamer.Warehouse.AddItemWithSlot(tempItem, srcSlot);
                                    }
                                }
                                else
                                {
                                    client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                                    sourceItem.SetItemId();
                                }
                            }

                        // Persistência apenas dos slots afetados
                        var updatedInventoryItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);
                        var updatedWarehouseItem = client.Tamer.Warehouse.FindItemBySlot(srcSlot);

                        if (updatedInventoryItem != null)
                            await _sender.Send(new UpdateItemCommand(updatedInventoryItem));
                        if (updatedWarehouseItem != null)
                            await _sender.Send(new UpdateItemCommand(updatedWarehouseItem));

                        return true;
                    }



                case ItemListMovimentationEnum.WarehouseToAccountWarehouse:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.Warehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.AccountWarehouse.FindItemBySlot(dstSlot);

                        if (sourceItem == null)
                            return false;

                        if (sourceItem.ItemInfo.BoundType == 2)
                        {
                            client.Disconnect();

                            // await _mapServer.CallDiscord(
                            //  $"{client.Tamer.Name} tentou movimentar um item não-transferível (Bound). Jogador desconectado.",
                            //   client,
                            //   "e06666",
                            //  "DISCONNECTED",
                            //  "1307434967667835023",
                            //  true
                            // );

                            return false;
                        }

                        lock (client.Tamer.Warehouse)
                            lock (client.Tamer.AccountWarehouse)
                            {
                                if (destItem != null && destItem.ItemId > 0)
                                {
                                    if (destItem.ItemId == sourceItem.ItemId)
                                    {
                                        var maxOverlap = destItem.ItemInfo.Overlap;
                                        if (destItem.Amount + sourceItem.Amount > maxOverlap)
                                        {
                                            var remainingSpace = maxOverlap - destItem.Amount;
                                            destItem.IncreaseAmount(remainingSpace);
                                            sourceItem.ReduceAmount(remainingSpace);
                                        }
                                        else
                                        {
                                            destItem.IncreaseAmount(sourceItem.Amount);
                                            sourceItem.ReduceAmount(sourceItem.Amount);
                                        }
                                    }
                                    else
                                    {
                                        var tempItem = (ItemModel)destItem.Clone();
                                        tempItem.SetItemInfo(destItem.ItemInfo);

                                        client.Tamer.AccountWarehouse.AddItemWithSlot(sourceItem, dstSlot);
                                        client.Tamer.Warehouse.AddItemWithSlot(tempItem, srcSlot);
                                    }
                                }
                                else
                                {
                                    client.Tamer.AccountWarehouse.AddItemWithSlot(sourceItem, dstSlot);
                                    sourceItem.SetItemId();
                                }
                            }

                        var updatedWarehouseItem = client.Tamer.Warehouse.FindItemBySlot(srcSlot);
                        var updatedAccountWarehouseItem = client.Tamer.AccountWarehouse.FindItemBySlot(dstSlot);

                        if (updatedWarehouseItem != null)
                            await _sender.Send(new UpdateItemCommand(updatedWarehouseItem));
                        if (updatedAccountWarehouseItem != null)
                            await _sender.Send(new UpdateItemCommand(updatedAccountWarehouseItem));

                        var itemDetails = $"**{sourceItem.ItemInfo.Name}** x{sourceItem.Amount} movimentado do Armazém para o Armazém de Conta.";
                        await _mapServer.CallDiscord(
                            $"**Movimentação de Item**\n" +
                            $"**Player**: {client.Tamer.Name} movimentou o item {itemDetails}",
                            client,
                            "00ff00",
                            "ITEM MOVEMENT",
                            "1374533026142486739"
                        );

                        return true;
                    }





                case ItemListMovimentationEnum.AccountWarehouseToInventory:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.InventoryMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.AccountWarehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Inventory.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            var tempDestItem = (ItemModel)destItem.Clone();
                            tempDestItem.SetItemInfo(destItem.ItemInfo);

                            var tempSourceItem = (ItemModel)sourceItem.Clone();
                            tempSourceItem.SetItemInfo(sourceItem.ItemInfo);

                            if (destItem.ItemId == sourceItem.ItemId)
                            {
                                if (destItem.Amount == destItem.ItemInfo.Overlap)
                                {
                                    client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                                    client.Tamer.AccountWarehouse.AddItemWithSlot(tempDestItem, srcSlot);

                                    client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                    client.Tamer.Inventory.AddItemWithSlot(tempSourceItem, dstSlot);
                                }
                                else
                                {
                                    int remainingSpace = destItem.ItemInfo.Overlap - destItem.Amount;

                                    if (remainingSpace >= sourceItem.Amount)
                                    {
                                        tempDestItem.Amount = tempDestItem.Amount + sourceItem.Amount;

                                        client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);

                                        client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                        client.Tamer.Inventory.AddItemWithSlot(tempDestItem, dstSlot);
                                    }
                                    else
                                    {
                                        tempSourceItem.Amount = tempSourceItem.Amount - remainingSpace;
                                        tempDestItem.Amount = tempDestItem.Amount + remainingSpace;

                                        client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                                        client.Tamer.AccountWarehouse.AddItemWithSlot(tempSourceItem, srcSlot);

                                        client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                        client.Tamer.Inventory.AddItemWithSlot(tempDestItem, dstSlot);
                                    }
                                }
                            }
                            else
                            {
                                client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                                client.Tamer.AccountWarehouse.AddItemWithSlot(tempDestItem, srcSlot);

                                client.Tamer.Inventory.RemoveOrReduceItemWithSlot(destItem, dstSlot);
                                client.Tamer.Inventory.AddItemWithSlot(tempSourceItem, dstSlot);
                            }
                        }
                        else
                        {
                            client.Tamer.Inventory.AddItemWithSlot(sourceItem, dstSlot);
                            client.Tamer.AccountWarehouse.RemoveOrReduceItemWithSlot(sourceItem, srcSlot);
                        }

                        return true;
                    }

                case ItemListMovimentationEnum.AccountWarehouseToWarehouse:
                    {
                        var srcSlot = originSlot - GeneralSizeEnum.AccountWarehouseMinSlot.GetHashCode();
                        var dstSlot = destinationSlot - GeneralSizeEnum.WarehouseMinSlot.GetHashCode();

                        var sourceItem = client.Tamer.AccountWarehouse.FindItemBySlot(srcSlot);
                        var destItem = client.Tamer.Warehouse.FindItemBySlot(dstSlot);

                        if (destItem.ItemId > 0)
                        {
                            var tempItem = (ItemModel)destItem.Clone();
                            tempItem.SetItemInfo(destItem.ItemInfo);

                            if (destItem.ItemId == sourceItem.ItemId)
                            {
                                destItem.IncreaseAmount(sourceItem.Amount);
                                sourceItem.ReduceAmount(sourceItem.Amount);
                            }
                            else
                            {
                                client.Tamer.Warehouse.AddItemWithSlot(sourceItem, dstSlot);
                                client.Tamer.AccountWarehouse.AddItemWithSlot(tempItem, srcSlot);
                            }
                        }
                        else
                        {
                            client.Tamer.Warehouse.AddItemWithSlot(sourceItem, dstSlot);
                            sourceItem.SetItemId();
                        }

                        return true;
                    }
            }

            return false;
        }
    }
}