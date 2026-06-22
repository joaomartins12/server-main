using System.Net.Http.Headers;
using AutoMapper;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Game.Managers;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class DigimonDataExchangePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.DigimonDataExchange;

        private readonly StatusManager _statusManager;
        private readonly IMapper _mapper;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public DigimonDataExchangePacketProcessor(
            StatusManager statusManager,
            IMapper mapper,
            ILogger logger,
            ISender sender)
        {
            _statusManager = statusManager;
            _mapper = mapper;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var npcId = packet.ReadInt();
            var nDataChangeType = (DigimonDataExchangeEnum)packet.ReadInt();
            var leftDigiviceSlot = packet.ReadByte();
            var rightDigiviceSlot = packet.ReadByte();

            var leftDigimon = client.Tamer.Digimons.FirstOrDefault(x => x.Slot == leftDigiviceSlot);
            var rightDigimon = client.Tamer.Digimons.FirstOrDefault(x => x.Slot == rightDigiviceSlot);

            var itemToRemove = client.Tamer.Inventory.FindItemById(19991) ?? client.Tamer.Inventory.FindItemById(19992);
            if(itemToRemove.Amount > 0)
            {
                switch (nDataChangeType)
                {
                    case DigimonDataExchangeEnum.eDataChangeType_Size:
                        ProcessSizeChange(client, npcId, leftDigiviceSlot, rightDigiviceSlot, leftDigimon, rightDigimon);
                        break;

                    case DigimonDataExchangeEnum.eDataChangeType_Inchant:
                        ProcessEnchant(client, npcId, leftDigiviceSlot, rightDigiviceSlot, leftDigimon, rightDigimon);
                        break;

                    case DigimonDataExchangeEnum.eDataChangeType_EvoSlot:
                        ProcessEvoSlot(client, npcId, leftDigiviceSlot, rightDigiviceSlot, leftDigimon, rightDigimon);
                        break;

                    default:
                        client.Send(new DigimonDataExchangePacket(nDataChangeType, (int)DigimonDataExchangeResultEnum.NONE_SLOT, leftDigiviceSlot, rightDigiviceSlot));
                        break;
                }

                client.Tamer.Inventory.RemoveOrReduceItem(itemToRemove, 1);
                await _sender.Send(new UpdateItemCommand(itemToRemove));
            }
            else
            {
                client.Disconnect();
            }
        }
        private async Task ProcessSizeChange(GameClient client, int npcId, int leftDigiviceSlot, int rightDigiviceSlot, DigimonModel leftDigimon, DigimonModel rightDigimon)
        {
            var leftActualSize = leftDigimon.Size;
            var rightActualSize = rightDigimon.Size;

            leftDigimon.SetSize(rightActualSize);
            rightDigimon.SetSize(leftActualSize);

            var result = (int)DigimonDataExchangeResultEnum.MESSAGE_COMPLETE;

            _logger.Information($"Character {client.Tamer.Name} swap partner size of {leftDigimon.Name} Size=({leftActualSize}) to Size={leftDigimon.Size}" +
                $"from {rightDigimon.Name} Size=({rightActualSize}) to Size={rightDigimon.Size}.");

            client.Send(new DigimonDataExchangePacket(DigimonDataExchangeEnum.eDataChangeType_Size, result, (byte)leftDigiviceSlot, (byte)rightDigiviceSlot, leftDigimon, rightDigimon));

            await _sender.Send(new UpdateDigimonSizeCommand(leftDigimon.Id, leftDigimon.Size));
            await _sender.Send(new UpdateDigimonSizeCommand(rightDigimon.Id, rightDigimon.Size));
        }

        private async Task ProcessEnchant(GameClient client, int npcId, int leftDigiviceSlot, int rightDigiviceSlot, DigimonModel leftDigimon, DigimonModel rightDigimon)
        {
            var result = (int)DigimonDataExchangeResultEnum.MESSAGE_COMPLETE;

            var leftDigimonCloneActualLevel = leftDigimon.Digiclone.CloneLevel;
            var leftDigimonCloneATActualLevel = leftDigimon.Digiclone.ATLevel;
            var leftDigimonCloneBLActualLevel = leftDigimon.Digiclone.BLLevel;
            var leftDigimonCloneCTActualLevel = leftDigimon.Digiclone.CTLevel;
            var leftDigimonCloneHPActualLevel = leftDigimon.Digiclone.HPLevel;
            var leftDigimonCloneEVActualLevel = leftDigimon.Digiclone.EVLevel;
            var leftDigimonCloneATActualValue = leftDigimon.Digiclone.ATValue;
            var leftDigimonCloneBLActualValue = leftDigimon.Digiclone.BLValue;
            var leftDigimonCloneCTActualValue = leftDigimon.Digiclone.CTValue;
            var leftDigimonCloneHPActualValue = leftDigimon.Digiclone.HPValue;
            var leftDigimonCloneEVActualValue = leftDigimon.Digiclone.EVValue;

            var rightDigimonCloneActualLevel = rightDigimon.Digiclone.CloneLevel;
            var rightDigimonCloneATActualLevel = rightDigimon.Digiclone.ATLevel;
            var rightDigimonCloneBLActualLevel = rightDigimon.Digiclone.BLLevel;
            var rightDigimonCloneCTActualLevel = rightDigimon.Digiclone.CTLevel;
            var rightDigimonCloneHPActualLevel = rightDigimon.Digiclone.HPLevel;
            var rightDigimonCloneEVActualLevel = rightDigimon.Digiclone.EVLevel;
            var rightDigimonCloneATActualValue = rightDigimon.Digiclone.ATValue;
            var rightDigimonCloneBLActualValue = rightDigimon.Digiclone.BLValue;
            var rightDigimonCloneCTActualValue = rightDigimon.Digiclone.CTValue;
            var rightDigimonCloneHPActualValue = rightDigimon.Digiclone.HPValue;
            var rightDigimonCloneEVActualValue = rightDigimon.Digiclone.EVValue;

            leftDigimon.Digiclone.SetDigicloneValue(DigicloneTypeEnum.AT, rightDigimonCloneATActualValue, rightDigimonCloneATActualLevel);
            leftDigimon.Digiclone.SetDigicloneValue(DigicloneTypeEnum.BL, rightDigimonCloneBLActualValue, rightDigimonCloneBLActualLevel);
            leftDigimon.Digiclone.SetDigicloneValue(DigicloneTypeEnum.CT, rightDigimonCloneCTActualValue, rightDigimonCloneCTActualLevel);
            leftDigimon.Digiclone.SetDigicloneValue(DigicloneTypeEnum.HP, rightDigimonCloneHPActualValue, rightDigimonCloneHPActualLevel);
            leftDigimon.Digiclone.SetDigicloneValue(DigicloneTypeEnum.EV, rightDigimonCloneEVActualValue, rightDigimonCloneEVActualLevel);

            rightDigimon.Digiclone.SetDigicloneValue(DigicloneTypeEnum.AT, leftDigimonCloneATActualValue, leftDigimonCloneATActualLevel);
            rightDigimon.Digiclone.SetDigicloneValue(DigicloneTypeEnum.BL, leftDigimonCloneBLActualValue, leftDigimonCloneBLActualLevel);
            rightDigimon.Digiclone.SetDigicloneValue(DigicloneTypeEnum.CT, leftDigimonCloneCTActualValue, leftDigimonCloneCTActualLevel);
            rightDigimon.Digiclone.SetDigicloneValue(DigicloneTypeEnum.HP, leftDigimonCloneHPActualValue, leftDigimonCloneHPActualLevel);
            rightDigimon.Digiclone.SetDigicloneValue(DigicloneTypeEnum.EV, leftDigimonCloneEVActualValue, leftDigimonCloneEVActualLevel);

            client.Send(new DigimonDataExchangePacket(DigimonDataExchangeEnum.eDataChangeType_Inchant, result, (byte)leftDigiviceSlot, (byte)rightDigiviceSlot, leftDigimon, rightDigimon));

            await _sender.Send(new UpdateDigicloneCommand(leftDigimon.Digiclone));
            await _sender.Send(new UpdateDigicloneCommand(rightDigimon.Digiclone));
        }
        private async Task ProcessEvoSlot(GameClient client, int npcId, int leftDigiviceSlot, int rightDigiviceSlot, DigimonModel leftDigimon, DigimonModel rightDigimon)
        {
            var result = (int)DigimonDataExchangeResultEnum.MESSAGE_COMPLETE;

            // Salva os dados anteriores antes da troca
            var leftDigimonUnlockedEvolutions = leftDigimon.Evolutions.Select(evo => new { evo.Type, evo.Unlocked }).ToList();
            var rightDigimonUnlockedEvolutions = rightDigimon.Evolutions.Select(evo => new { evo.Type, evo.Unlocked }).ToList();

            // Troca de evoluções
            foreach (var rightEvo in rightDigimon.Evolutions)
            {
                var matchingLeftEvo = leftDigimonUnlockedEvolutions.FirstOrDefault(evo => evo.Type == rightEvo.Type);
                if (matchingLeftEvo != null)
                {
                    rightEvo.SetUnlocked(matchingLeftEvo.Unlocked);
                    await _sender.Send(new UpdateEvolutionCommand(rightEvo));
                }
            }
            foreach (var leftEvo in leftDigimon.Evolutions)
            {
                var matchingRightEvo = rightDigimonUnlockedEvolutions.FirstOrDefault(evo => evo.Type == leftEvo.Type);
                if (matchingRightEvo != null)
                {
                    leftEvo.SetUnlocked(matchingRightEvo.Unlocked);
                    await _sender.Send(new UpdateEvolutionCommand(leftEvo));
                }
            }

            client.Send(new DigimonDataExchangePacket(DigimonDataExchangeEnum.eDataChangeType_EvoSlot, result, (byte)leftDigiviceSlot, (byte)rightDigiviceSlot, leftDigimon, rightDigimon));
        }

    }
}
