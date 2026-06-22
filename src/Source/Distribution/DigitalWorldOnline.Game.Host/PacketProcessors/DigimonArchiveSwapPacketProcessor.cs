using AutoMapper;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Writers;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class DigimonArchiveSwapPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.DigimonArchiveSwap;

        private readonly StatusManager _statusManager;
        private readonly IMapper _mapper;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public DigimonArchiveSwapPacketProcessor(
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
            var pack = new GamePacketReader(packetData);

            var vipEnabled = Convert.ToBoolean(pack.ReadByte());
            var oldSlot = pack.ReadInt() - 1000;
            var newSlot = pack.ReadInt() - 1000;
            int npcId = pack.ReadInt();

            // Evita troca no mesmo slot
            if (oldSlot == newSlot)
            {
                client.Send(new SystemMessagePacket("Você não pode trocar o mesmo slot."));
                return;
            }

            // Busca os slots
            var oldSlotItem = client.Tamer.DigimonArchive.DigimonArchives.FirstOrDefault(x => x.Slot == oldSlot);
            var newSlotItem = client.Tamer.DigimonArchive.DigimonArchives.FirstOrDefault(x => x.Slot == newSlot);

            // Verifica existência
            if (oldSlotItem == null || newSlotItem == null)
            {
                client.Send(new SystemMessagePacket("Um dos slots de Digimon não foi encontrado."));
                return;
            }

            // Armazena os IDs antes da remoção
            var oldDigimonId = oldSlotItem.DigimonId;
            var newDigimonId = newSlotItem.DigimonId;

            // Impede troca vazia com vazia
            if (oldDigimonId == 0 && newDigimonId == 0)
            {
                client.Send(new SystemMessagePacket("Ambos os slots estão vazios."));
                return;
            }

            // Remove os digimons
            oldSlotItem.RemoveDigimon();
            newSlotItem.RemoveDigimon();

            // Adiciona nos slots invertidos
            oldSlotItem.AddDigimon(newDigimonId);
            newSlotItem.AddDigimon(oldDigimonId);

            // Persiste no banco
            await _sender.Send(new UpdateCharacterDigimonArchiveItemCommand(oldSlotItem));
            await _sender.Send(new UpdateCharacterDigimonArchiveItemCommand(newSlotItem));

            // Envia resposta pro cliente
            client.Send(new DigimonArchivePacket(oldSlot, newSlot));
        }
    }
}
