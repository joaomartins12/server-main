using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchIncreasePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchIncrease;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly AssetsLoader _assets;
        private readonly ConfigsLoader _configs;
        private readonly ISender _sender;

        public HatchIncreasePacketProcessor(
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            AssetsLoader assets,
            ConfigsLoader configs,
            ILogger logger,   // mantido na assinatura para compatibilidade com DI
            ISender sender
        )
        {
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _assets = assets;
            _configs = configs;
            _sender = sender;
            _ = logger; // evita warning de parâmetro não usado, sem emitir logs
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var vipEnabled = packet.ReadByte();
            var npcId = packet.ReadInt();
            var dataTier = packet.ReadByte(); // 0=Low, 1=Mid

            var targetItem = client.Tamer.Incubator.EggId;
            var hatchInfo = _assets.Hatchs.FirstOrDefault(x => x.ItemId == targetItem);
            if (hatchInfo == null)
            {
                client.Send(new SystemMessagePacket($"Unknown hatch info for egg {targetItem}."));
                return;
            }

            var currentLevel = client.Tamer.Incubator.HatchLevel; // 0..4
            var nextLevel = currentLevel + 1;                     // 1..5

            // — CONDIÇÕES POR EGGTYPE —
            (int guaranteedMax, bool isBinary100) ResolveGuarantee()
            {
                switch (hatchInfo.EggType)
                {
                    case 1: return (5, true);   // 100% total (1→5)
                    case 3: return (3, false);  // 100% até 3/5
                    case 4: return (4, false);  // 100% até 4/5
                    case 5: return (5, false);  // 100% até 5/5
                    case 0:
                    default:
                        // Fallback: EggType=0 mas breakpoints indicam garantia
                        if (hatchInfo.LowClassBreakPoint == 0 &&
                            hatchInfo.MidClassBreakPoint >= 3 && hatchInfo.MidClassBreakPoint <= 5)
                            return (hatchInfo.MidClassBreakPoint, false);
                        return (0, false); // normal
                }
            }

            var (guaranteedMax, isBinary100) = ResolveGuarantee();
            var withinGuaranteed = guaranteedMax > 0 && nextLevel <= guaranteedMax;
            var alwaysSucceedByAsset = hatchInfo.LowClassBreakPoint == hatchInfo.MidClassBreakPoint;

            var hatchConfig = _configs.Hatchs.FirstOrDefault(x => x.Type.GetHashCode() == nextLevel);
            if (hatchConfig == null)
            {
                client.Send(new HatchIncreaseFailedPacket(client.Tamer.GeneralHandler, HatchIncreaseResultEnum.Failled));
                client.Send(new SystemMessagePacket($"Invalid hatch config for level {nextLevel}."));
                return;
            }

            // Em legado (3/4/5), se tentar ACIMA do garantido com LowData → falha forçada (sem consumir)
            if (!withinGuaranteed && guaranteedMax > 0 && !isBinary100 && dataTier == 0)
            {
                client.Send(new HatchIncreaseFailedPacket(client.Tamer.GeneralHandler, HatchIncreaseResultEnum.Failled));
                client.Send(new SystemMessagePacket("Use MidData to continue. Failed forced!"));
                return;
            }

            // Consumo de dados
            bool removed;
            if (dataTier == 0)
            {
                removed = client.Tamer.Inventory.RemoveOrReduceItemsBySection(hatchInfo.LowClassDataSection, hatchInfo.LowClassDataAmount);
                if (!removed)
                {
                    client.Send(new HatchIncreaseFailedPacket(client.Tamer.GeneralHandler, HatchIncreaseResultEnum.Failled));
                    client.Send(new SystemMessagePacket($"Invalid low class data amount for egg {targetItem} and section {hatchInfo.LowClassDataSection}."));
                    return;
                }
            }
            else
            {
                removed = client.Tamer.Inventory.RemoveOrReduceItemsBySection(hatchInfo.MidClassDataSection, hatchInfo.MidClassDataAmount);
                if (!removed)
                {
                    client.Send(new HatchIncreaseFailedPacket(client.Tamer.GeneralHandler, HatchIncreaseResultEnum.Failled));
                    client.Send(new SystemMessagePacket($"Invalid mid class data amount for egg {targetItem} and section {hatchInfo.MidClassDataSection}."));
                    return;
                }
            }

            void BroadcastSuccess()
            {
                if (client.DungeonMap)
                {
                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new HatchIncreaseSucceedPacket(client.Tamer.GeneralHandler, client.Tamer.Incubator.HatchLevel).Serialize());
                }
                else
                {
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new HatchIncreaseSucceedPacket(client.Tamer.GeneralHandler, client.Tamer.Incubator.HatchLevel).Serialize());
                }
            }
            void BroadcastFail(HatchIncreaseResultEnum result)
            {
                if (client.DungeonMap)
                {
                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new HatchIncreaseFailedPacket(client.Tamer.GeneralHandler, result).Serialize());
                }
                else
                {
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                        new HatchIncreaseFailedPacket(client.Tamer.GeneralHandler, result).Serialize());
                }
            }

            // — Caminho garantido —
            if (isBinary100 || withinGuaranteed || alwaysSucceedByAsset)
            {
                client.Tamer.Incubator.IncreaseLevel();
                BroadcastSuccess();

                client.Tamer.Incubator.RemoveBackupDisk();

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));
                return;
            }

            // — Caminho probabilístico (normal ou acima do garantido) —
            if (hatchConfig.SuccessChance >= UtilitiesFunctions.RandomDouble())
            {
                client.Tamer.Incubator.IncreaseLevel();
                BroadcastSuccess();
            }
            else
            {
                if (hatchConfig.BreakChance >= UtilitiesFunctions.RandomDouble())
                {
                    if (client.Tamer.Incubator.BackupDiskId > 0)
                    {
                        BroadcastFail(HatchIncreaseResultEnum.Backuped);
                    }
                    else
                    {
                        BroadcastFail(HatchIncreaseResultEnum.Broken);
                        client.Tamer.Incubator.RemoveEgg();
                    }
                }
                else
                {
                    BroadcastFail(HatchIncreaseResultEnum.Failled);
                }
            }

            client.Tamer.Incubator.RemoveBackupDisk();

            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
            await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));
        }
    }
}
