using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Utils;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ItemRerollPacketProcessor : IGamePacketProcessor
    {
        private static readonly List<int> _normalStone = new()
        {
            45003, 46996, 46998, 47003, 47007
        };

        private static readonly List<int> _advancedStone = new()
        {
            47008 , 47106
        };

        private static readonly List<int> _digitaryStone = new()
        {
            45000, 47000
        };

        private static readonly List<int> _highStone = new()
        {
            46997, 46999
        };

        private static readonly List<int> _shinyStone = new()
        {
            10026, 47004
        };

        private static readonly List<int> _amazingStone = new()
        {
            10259, 47009, 47107
        };

        private static readonly List<int> _optionNumberChange = new()
        {
            10052, 45001, 47001, 47005
        };
        private static readonly List<int> MegaOption = new()
        {
            40, 59089
        };
        private static readonly List<int> _amazingStoneDTU = new()
        {
            59,
        };
        private static readonly List<int> _NunbemStoneDTU = new()
        {
            59022,
        };

        private static readonly List<int> _numberChange = new()
        {
            10053, 45002, 47002, 47006
        };

        private readonly List<StatusLimit> StatusLimit;

        public GameServerPacketEnum Type => GameServerPacketEnum.ItemReroll;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public ItemRerollPacketProcessor(
            AssetsLoader assets,
            ISender sender,
            ILogger logger)
        {
            _assets = assets;
            _sender = sender;
            _logger = logger;

            StatusLimit = new List<StatusLimit>()
            {
                new StatusLimit(AccessoryTypeEnum.Ring, AccessoryStatusTypeEnum.HP, 3),
                new StatusLimit(AccessoryTypeEnum.Ring, AccessoryStatusTypeEnum.DS, 3),
                new StatusLimit(AccessoryTypeEnum.Ring, AccessoryStatusTypeEnum.AT, 2),
                new StatusLimit(AccessoryTypeEnum.Ring, AccessoryStatusTypeEnum.CT, 2),
                new StatusLimit(AccessoryTypeEnum.Ring, AccessoryStatusTypeEnum.DE, 2),
                new StatusLimit(AccessoryTypeEnum.Ring, AccessoryStatusTypeEnum.ATT, 2),
                new StatusLimit(AccessoryTypeEnum.Ring, AccessoryStatusTypeEnum.SCD, 2),
                new StatusLimit(AccessoryTypeEnum.Ring, AccessoryStatusTypeEnum.CD, 1),

                new StatusLimit(AccessoryTypeEnum.Necklace, AccessoryStatusTypeEnum.HP, 3),
                new StatusLimit(AccessoryTypeEnum.Necklace, AccessoryStatusTypeEnum.DS, 3),
                new StatusLimit(AccessoryTypeEnum.Necklace, AccessoryStatusTypeEnum.AT, 1),
                new StatusLimit(AccessoryTypeEnum.Necklace, AccessoryStatusTypeEnum.AS, 1),
                new StatusLimit(AccessoryTypeEnum.Necklace, AccessoryStatusTypeEnum.CD, 1),
                new StatusLimit(AccessoryTypeEnum.Necklace, AccessoryStatusTypeEnum.CT, 2),
                new StatusLimit(AccessoryTypeEnum.Necklace, AccessoryStatusTypeEnum.DE, 2),
                new StatusLimit(AccessoryTypeEnum.Necklace, AccessoryStatusTypeEnum.ATT, 2),
                new StatusLimit(AccessoryTypeEnum.Necklace, AccessoryStatusTypeEnum.SCD, 1),

                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.HP, 2),
                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.AT, 1),
                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.DS, 2),
                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.HT, 1),
                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.BL, 1),
                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.CD, 2),
                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.CT, 1),
                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.DE, 2),
                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.ATT, 2),
                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.SCD, 1),
                new StatusLimit(AccessoryTypeEnum.Earring, AccessoryStatusTypeEnum.EV, 1),

                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.HP, 2),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.DS, 2),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.AT, 1),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.DE, 1),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.CD, 2),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.AS, 1),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.CT, 2),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.BL, 2),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.SCD, 1),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.HT, 2),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.EV, 2),
                new StatusLimit(AccessoryTypeEnum.Bracelet, AccessoryStatusTypeEnum.ATT, 1),

                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.HP, 2),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.DS, 2),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.AT, 2),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.DE, 2),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.AS, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.HT, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.CD, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.ATT, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.SCD, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Data, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Vacina, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Virus, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Unknown, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Ice, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Water, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Fire, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Earth, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Wind, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Wood, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Light, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Dark, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Thunder, 1),
                new StatusLimit(AccessoryTypeEnum.Digivice, AccessoryStatusTypeEnum.Steel, 1)
            };
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var tamerHandle = packet.ReadInt();
            var consumableSlot = packet.ReadShort();
            var accessorySlot = packet.ReadShort();
            var changedStatusSlot = packet.ReadByte();

            var consumedStone = client.Tamer.Inventory.FindItemBySlot(consumableSlot);
            if (consumedStone == null || consumedStone.ItemId == 0)
            {
                _logger.Warning($"Invalid item at slot {consumableSlot} for tamer {client.TamerId}.");
                client.Send(UtilitiesFunctions.GroupPackets(
                        new SystemMessagePacket($"Invalid stone at slot {consumableSlot}.").Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                return;
            }

            var targetAccessory = client.Tamer.Inventory.FindItemBySlot(accessorySlot);
            if (targetAccessory == null || targetAccessory.ItemId == 0)
            {
                _logger.Warning($"Invalid item at slot {accessorySlot} for tamer {client.TamerId}.");
                client.Send(UtilitiesFunctions.GroupPackets(
                        new SystemMessagePacket($"Invalid accessory at slot {accessorySlot}.").Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                return;
            }

            byte result = 1; //1 = success | 2 = fail | 3 = dont change

            if (_normalStone.Contains(consumedStone.ItemId))
            {
                if (targetAccessory.RerollLeft >= 20)
                {
                    result = 3;

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                    return;
                }
                else
                {
                    result = UtilitiesFunctions.RandomByte(0, 1);

                    targetAccessory.RerollLeft += result;

                    client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));


                }
            }

            if (_advancedStone.Contains(consumedStone.ItemId))
            {
                if (targetAccessory.RerollLeft + 5 > 20)
                {
                    result = 3;

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                    return;
                }
                else
                {
                    targetAccessory.RerollLeft += 5;

                    client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                }
            }

            if (_digitaryStone.Contains(consumedStone.ItemId))
            {
                if (targetAccessory.Power >= 200)
                {
                    result = 3;

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                    return;
                }
                else
                {
                    result = UtilitiesFunctions.RandomByte(1, 3);

                    if (result == 1)
                    {
                        targetAccessory.Power += 1;
                    }
                    else if (result == 2)
                    {
                        targetAccessory.Power -= 1;
                    }
                    else
                    {
                    }

                    client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                }
            }

            if (_highStone.Contains(consumedStone.ItemId))
            {
                if (targetAccessory.Power + 2 > 200)
                {
                    result = 3;

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                    return;
                }
                else
                {
                    result = UtilitiesFunctions.RandomByte(1, 3);

                    if (result == 1)
                    {
                        targetAccessory.Power += 2;
                    }
                    else if (result == 2)
                    {
                        targetAccessory.Power -= 2;
                    }
                    else
                    {
                    }

                    client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                }
            }

            if (_shinyStone.Contains(consumedStone.ItemId))
            {
                if (targetAccessory.Power + 3 > 200)
                {
                    result = 3;

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));

                    return;
                }
                else
                {
                    targetAccessory.Power += 3;

                    client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));

                }
            }

            if (_amazingStone.Contains(consumedStone.ItemId))
            {
                if (targetAccessory.Power + 10 > 200)
                {
                    result = 3;

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));

                    return;
                }
                else
                {
                    targetAccessory.Power += 10;

                    client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));

                }
            }

            if (_optionNumberChange.Contains(consumedStone.ItemId))
            {
                result = 0;

                // Verifica se ainda há rerolls disponíveis
                if (targetAccessory.RerollLeft == 0)
                {
                    result = 3;
                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                    _logger.Debug($"Character {client.TamerId} cannot reroll item {targetAccessory.ItemId} status with {consumedStone.ItemId}.");
                    return;
                }

                // Decrementa o número de rerolls restantes
                targetAccessory.RerollLeft -= 1;

                // Obtém as informações do item
                var itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == targetAccessory.ItemId);
                if (itemInfo == null)
                {
                    result = 3;
                    client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);
                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                            new SystemMessagePacket($"Invalid item info for item {targetAccessory.ItemId}.").Serialize()));
                    _logger.Warning($"Invalid item info for item {targetAccessory.ItemId} and tamer {client.TamerId}.");
                    return;
                }

                // Obtém os assets do acessório
                var accessoryAsset = _assets.AccessoryRoll.FirstOrDefault(x => x.ItemId == targetAccessory.ItemId);
                if (accessoryAsset != null)
                {
                    // Ordena os status por slot
                    targetAccessory.AccessoryStatus = targetAccessory.AccessoryStatus.OrderBy(x => x.Slot).ToList();

                    // Captura o status antigo
                    var oldStatusString = targetAccessory.AccessoryStatus
                        .Where(x => x.Value > 0)
                        .Select(x => $"{x.Type} ({x.Value})")
                        .ToList();

                    // Aplica os novos status
                    for (int i = 0; i < accessoryAsset.StatusAmount; i++)
                    {
                        var forbiddenStatusType = new List<AccessoryStatusTypeEnum>();
                        if (accessoryAsset.StatusAmount > 1)
                        {
                            foreach (AccessoryStatusTypeEnum statusType in Enum.GetValues(typeof(AccessoryStatusTypeEnum)))
                            {
                                var currentAmount = targetAccessory.StatusAmount(statusType);
                                if (currentAmount >= StatusLimit.FirstOrDefault(x => x.Accessory == (AccessoryTypeEnum)itemInfo.Type && x.Status == statusType)?.MaxAmount)
                                {
                                    forbiddenStatusType.Add(statusType);
                                }
                            }
                        }

                        var possibleStatus = accessoryAsset.Status.Where(x => !forbiddenStatusType.Contains((AccessoryStatusTypeEnum)x.Type));
                        var newStatus = possibleStatus.OrderBy(x => Guid.NewGuid()).First();
                        targetAccessory.AccessoryStatus[i].SetType((AccessoryStatusTypeEnum)newStatus.Type);
                        targetAccessory.AccessoryStatus[i].SetValue(UtilitiesFunctions.RandomShort((short)newStatus.MinValue, (short)(newStatus.MaxValue)));
                    }

                    // Captura o status novo
                    var newStatusString = targetAccessory.AccessoryStatus
                        .Where(x => x.Value > 0)
                        .Select(x => $"{x.Type} ({x.Value})")
                        .ToList();

                    // Registra a alteração no log
                    _logger.Verbose($"Character {client.TamerId} rerolled status for item {targetAccessory.ItemId} with {consumedStone.ItemId} power {targetAccessory.Power} " +
                        $"reroll {targetAccessory.RerollLeft} and new status {string.Join(',', newStatusString)}.");

                    // Envia mensagem no chat sem cor
                    var chatMessage = $"Old status: {string.Join(", ", oldStatusString)} | New status: {string.Join(", ", newStatusString)}";
                    client.Send(new ChatMessagePacket(chatMessage, ChatTypeEnum.Shout, "System", 52, 120).Serialize());

                    // Remove ou reduz o item consumido
                    client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                    // Envia os pacotes atualizados ao cliente
                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                }
                else
                {
                    result = 3;
                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                            new SystemMessagePacket($"Invalid accessory asset for item {targetAccessory.ItemId}.").Serialize()));
                    _logger.Warning($"Invalid accessory asset with item id {targetAccessory.ItemId} for tamer {client.TamerId}.");
                    return;
                }
            }

            if (MegaOption.Contains(consumedStone.ItemId))
            {
                result = 0;

                // Verifica se ainda há rerolls disponíveis
                if (targetAccessory.RerollLeft == 0)
                {
                    result = 3;
                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                    _logger.Debug($"Character {client.TamerId} cannot reroll item {targetAccessory.ItemId} status with {consumedStone.ItemId}.");
                    return;
                }

                // Captura o status atual antes da alteração
                var currentStatus = targetAccessory.AccessoryStatus.FirstOrDefault(s => s.Slot == changedStatusSlot);
                if (currentStatus == null)
                {
                    _logger.Warning($"Invalid status slot {changedStatusSlot} for item {targetAccessory.ItemId}.");
                    return;
                }
                var oldType = currentStatus.Type;
                var oldValue = currentStatus.Value;

                // Obtém as informações do item
                var itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == targetAccessory.ItemId);
                if (itemInfo == null)
                {
                    result = 3;
                    client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);
                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                            new SystemMessagePacket($"Invalid item info for item {targetAccessory.ItemId}.").Serialize()));
                    _logger.Warning($"Invalid item info for item {targetAccessory.ItemId} and tamer {client.TamerId}.");
                    return;
                }

                // Obtém o asset do acessório
                var accessoryAsset = _assets.AccessoryRoll.FirstOrDefault(x => x.ItemId == targetAccessory.ItemId);
                if (accessoryAsset == null)
                {
                    result = 3;
                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                            new SystemMessagePacket($"Invalid accessory asset for item {targetAccessory.ItemId}.").Serialize()));
                    _logger.Warning($"Invalid accessory asset with item id {targetAccessory.ItemId} for tamer {client.TamerId}.");
                    return;
                }

                // Ordena os status pelo slot
                targetAccessory.AccessoryStatus = targetAccessory.AccessoryStatus.OrderBy(x => x.Slot).ToList();

                // Define o tipo de acessório
                var currentAccessoryType = (AccessoryTypeEnum)itemInfo.Type;

                // Filtra os status válidos
                var validStatus = accessoryAsset.Status.Where(newStatus =>
                {
                    if (newStatus.Type == (int)currentStatus.Type)
                        return false;

                    var limit = StatusLimit.FirstOrDefault(limit =>
                        limit.Accessory == currentAccessoryType &&
                        limit.Status == (AccessoryStatusTypeEnum)newStatus.Type);

                    if (limit == null)
                        return false;

                    int currentCount = targetAccessory.AccessoryStatus.Count(s => s.Type == (AccessoryStatusTypeEnum)newStatus.Type);

                    return currentCount < limit.MaxAmount;
                }).ToList();

                if (!validStatus.Any())
                {
                    result = 3;
                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                            new SystemMessagePacket($"No valid status to reroll for slot {changedStatusSlot}.").Serialize()));
                    _logger.Warning($"No valid status to reroll for slot {changedStatusSlot} on item {targetAccessory.ItemId} for tamer {client.TamerId}.");
                    return;
                }

                // Escolhe um novo status aleatório
                var newStatus = validStatus.OrderBy(x => Guid.NewGuid()).First();

                // Aplica o novo status garantindo que não seja adicionado um novo status
                if (!targetAccessory.AccessoryStatus.Contains(currentStatus))
                {
                    _logger.Error($"Unexpected error: status {newStatus.Type} being added instead of replaced on item {targetAccessory.ItemId}.");
                    return;
                }
                currentStatus.SetType((AccessoryStatusTypeEnum)newStatus.Type);
                currentStatus.SetValue(UtilitiesFunctions.RandomShort((short)newStatus.MinValue, (short)newStatus.MaxValue));

                // Captura o valor após a alteração

                var newType = currentStatus.Type;
                var newValue = currentStatus.Value;

                // Remove ou reduz o item consumido
                client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                // Loga o valor antes e depois
                _logger.Verbose($"Rerolling status for item {targetAccessory.ItemId} (Slot {changedStatusSlot}). Status {oldType} ({oldValue}) foi trocado por {newType} ({newValue}).");

                // Envia os pacotes para o cliente com a informação do valor antes e depois
                client.Send(UtilitiesFunctions.GroupPackets(
                        new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                        new SystemMessagePacket($"Status {oldType} ({oldValue}) foi trocado por {newType} ({newValue}).").Serialize()));
            }

            if (_NunbemStoneDTU.Contains(consumedStone.ItemId))
            {
                result = 0;

                if (targetAccessory.RerollLeft == 0)
                {
                    result = 3;
                    client.Send(UtilitiesFunctions.GroupPackets(
                        new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                    _logger.Debug($"Character {client.TamerId} cannot reroll item {targetAccessory.ItemId} with {consumedStone.ItemId} (no rerolls left).");
                    return;
                }

                var currentStatus = targetAccessory.AccessoryStatus[changedStatusSlot];
                var oldValue = currentStatus.Value;
                targetAccessory.RerollLeft -= 1;

                var accessoryAsset = _assets.AccessoryRoll.FirstOrDefault(x => x.ItemId == targetAccessory.ItemId);
                if (accessoryAsset != null)
                {
                    targetAccessory.AccessoryStatus = targetAccessory.AccessoryStatus.OrderBy(x => x.Slot).ToList();
                    var newStatus = accessoryAsset.Status.FirstOrDefault(x => (AccessoryStatusTypeEnum)x.Type == currentStatus.Type);

                    if (newStatus == null)
                    {
                        result = 3;

                        client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                        client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                            new SystemMessagePacket($"Invalid accessory status reroll info for item {accessoryAsset.ItemId}.").Serialize()));

                        _logger.Warning($"Invalid accessory status for asset {accessoryAsset.Id} while tamer {client.TamerId} reroll.");
                        return;
                    }

                    // Aplica sempre o valor MÁXIMO
                    currentStatus.SetValue((short)newStatus.MaxValue);

                    var newValue = currentStatus.Value;

                    client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                    _logger.Verbose($"[MAX REROLL] Status reroll for item {targetAccessory.ItemId} (Slot {changedStatusSlot}). Old: {oldValue}, New: {newValue}.");

                    client.Send(UtilitiesFunctions.GroupPackets(
                        new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                        new SystemMessagePacket($"[Max Roll] Old: {oldValue}, New: {newValue}").Serialize()));
                }
                else
                {
                    result = 3;
                    client.Send(UtilitiesFunctions.GroupPackets(
                        new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                        new SystemMessagePacket($"Invalid accessory asset for item {targetAccessory.ItemId}.").Serialize()));
                    _logger.Warning($"Invalid accessory asset with item id {targetAccessory.ItemId} for tamer {client.TamerId}.");
                    return;
                }

                await _sender.Send(new UpdateItemAccessoryStatusCommand(targetAccessory));
                await _sender.Send(new UpdateItemCommand(consumedStone));
                return;
            }


            if (_amazingStoneDTU.Contains(consumedStone.ItemId))
            {
                byte rerollResult = 0;

                // Permitir uso apenas se o Power estiver em 200 ou mais
                if (targetAccessory.Power < 200)
                {
                    rerollResult = 3;
                    client.Send(UtilitiesFunctions.GroupPackets(
                        new ItemRerollPacket(rerollResult, accessorySlot, targetAccessory).Serialize(),
                        new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                    return;
                }

                // Aumenta exatamente 50 pontos por uso
                int increaseAmount = 50;
                int newPower = targetAccessory.Power + increaseAmount;

                // Limita o Power a no máximo 300
                if (newPower > 250)
                {
                    newPower = 250;
                }

                targetAccessory.Power = (byte)newPower;
                rerollResult = 1;

                // Remove a Amazing Stone do inventário
                client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                // Atualiza o inventário
                client.Send(UtilitiesFunctions.GroupPackets(
                    new ItemRerollPacket(rerollResult, accessorySlot, targetAccessory).Serialize(),
                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));

                _logger.Verbose($"✅ Character {client.TamerId} increased item {targetAccessory.ItemId} power by {increaseAmount}, new Power: {targetAccessory.Power}.");
            }

            if (_numberChange.Contains(consumedStone.ItemId))
            {
                result = 0;

                // Verifica se o personagem tem rerolls restantes
                if (targetAccessory.RerollLeft == 0)
                {
                    result = 3;
                    client.Send(UtilitiesFunctions.GroupPackets(
                            new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                            new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize()));
                    _logger.Debug($"Character {client.TamerId} cannot reroll item {targetAccessory.ItemId} status with {consumedStone.ItemId}.");
                    return;
                }
                else
                {
                    // Captura o valor antes da alteração
                    var currentStatus = targetAccessory.AccessoryStatus[changedStatusSlot];
                    var oldValue = currentStatus.Value;

                    targetAccessory.RerollLeft -= 1;

                    var accessoryAsset = _assets.AccessoryRoll.FirstOrDefault(x => x.ItemId == targetAccessory.ItemId);
                    if (accessoryAsset != null)
                    {
                        targetAccessory.AccessoryStatus = targetAccessory.AccessoryStatus.OrderBy(x => x.Slot).ToList();

                        var newStatus = accessoryAsset.Status.FirstOrDefault(x => (AccessoryStatusTypeEnum)x.Type == currentStatus.Type);

                        if (newStatus == null)
                        {
                            result = 3;

                            client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                            client.Send(UtilitiesFunctions.GroupPackets(
                                    new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                                    new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                                    new SystemMessagePacket($"Invalid accessory status reroll info for item {accessoryAsset.ItemId}.").Serialize()));

                            _logger.Warning($"Invalid accessory status for asset {accessoryAsset.Id} while tamer {client.TamerId} reroll.");
                            return;
                        }

                        // Aplica a mudança de status
                        currentStatus.SetValue(UtilitiesFunctions.RandomShort((short)newStatus.MinValue, (short)(newStatus.MaxValue)));

                        // Captura o valor após a alteração
                        var newValue = currentStatus.Value;

                        // Remove o item consumido
                        client.Tamer.Inventory.RemoveOrReduceItem(consumedStone, 1);

                        // Loga o valor antes e depois
                        _logger.Verbose($"Rerolling status for item {targetAccessory.ItemId} (Slot {changedStatusSlot}). Old value: {oldValue}, New value: {newValue}.");

                        // Envia os pacotes para o cliente com o valor antes e depois
                        client.Send(UtilitiesFunctions.GroupPackets(
                                new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                                new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                                new SystemMessagePacket($"Old Value: {oldValue}, New Value: {newValue}").Serialize()));
                    }
                    else
                    {
                        result = 3;

                        client.Send(UtilitiesFunctions.GroupPackets(
                                new ItemRerollPacket(result, accessorySlot, targetAccessory).Serialize(),
                                new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory).Serialize(),
                                new SystemMessagePacket($"Invalid accessory asset for item {targetAccessory.ItemId}.").Serialize()));

                        _logger.Warning($"Invalid accessory asset with item id {targetAccessory.ItemId} for tamer {client.TamerId}.");
                        return;
                    }
                }
            }

            await _sender.Send(new UpdateItemAccessoryStatusCommand(targetAccessory));
            await _sender.Send(new UpdateItemCommand(consumedStone));

        }
    }
}