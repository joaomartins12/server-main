using AutoMapper;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.GameServer;
using MediatR;
using Serilog;
using System.Linq;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class EncyclopediaLoadPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.EncyclopediaLoad;

        private readonly ISender _sender;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;

        public EncyclopediaLoadPacketProcessor(ISender sender, ILogger logger, IMapper mapper)
        {
            _sender = sender;
            _logger = logger;
            _mapper = mapper;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            _logger.Information("EncyclopediaLoad request received for CharacterId={CharacterId}", client.Tamer.Id);

            // 🔹 Todas as entries da encyclopedia
            var encyclopedia = client.Tamer.Encyclopedia;

            // 🔹 1. Digimons ativos (>=120 e Size >=128)
            var activeTypes = client.Tamer.Digimons
                .Where(d => d.Level >= 120 && d.Size >= 127)
                .Select(d => d.BaseType)
                .ToHashSet();

            // 🔹 2. Digimons do Archive (carregar info se necessário)
            var archiveTypes = new HashSet<int>();
            foreach (var digimonArchive in client.Tamer.DigimonArchive.DigimonArchives
                         .Where(x => x.DigimonId > 0))
            {
                if (digimonArchive.Digimon == null)
                {
                    // Buscar DTO do banco
                    var digimonDto = await _sender.Send(new GetDigimonByIdQuery(digimonArchive.DigimonId));

                    // Converter para Model
                    var digimonModel = _mapper.Map<DigimonModel>(digimonDto);

                    // Setar no archive
                    digimonArchive.SetDigimonInfo(digimonModel);
                }

                if (digimonArchive.Digimon != null &&
                    digimonArchive.Digimon.Level >= 120 &&
                    digimonArchive.Digimon.Size >= 127)
                {
                    archiveTypes.Add(digimonArchive.Digimon.BaseType);
                }
            }

            // 🔹 3. União de ativos + archive
            var allBaseTypes = activeTypes.Union(archiveTypes).ToHashSet();

            _logger.Information(
                "Character {CharacterId} has {Count} Digimons >=120 && Size>=127 (Active+Archive). BaseTypes: {Types}",
                client.Tamer.Id, allBaseTypes.Count, string.Join(",", allBaseTypes)
            );

            // 🔹 4. Filtrar encyclopedia
            var filtered = encyclopedia
                .Where(entry => entry.EvolutionAsset != null &&
                                allBaseTypes.Contains(entry.EvolutionAsset.Type))
                .ToList();

            _logger.Information(
                "Sending Encyclopedia with {Count} entries (>=120 && Size>=127) for CharacterId={CharacterId}",
                filtered.Count, client.Tamer.Id
            );

            // 🔹 5. Enviar
            client.Send(new EncyclopediaLoadPacket(filtered));
        }
    }
}
