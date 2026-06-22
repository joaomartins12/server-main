using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Commons.DTOs.Digimon;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchFinishPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchFinish;

        private readonly StatusManager _statusManager;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly AssetsLoader _assets;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IMapper _mapper;

        public HatchFinishPacketProcessor(
            StatusManager statusManager,
            AssetsLoader assets,
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            EventServer eventServer,
            PvpServer pvpServer,
            ILogger logger,
            ISender sender,
            IMapper mapper)
        {
            _statusManager = statusManager;
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);
            packet.Skip(5);
            var digiName = packet.ReadString();

            var hatchInfo = _assets.Hatchs.FirstOrDefault(x => x.ItemId == client.Tamer.Incubator.EggId);
            client.blockAchievement = false;

            if (hatchInfo == null)
            {
                _logger.Warning($"Unknown hatch info for egg {client.Tamer.Incubator.EggId}.");
                client.Send(new SystemMessagePacket($"Unknown hatch info for egg {client.Tamer.Incubator.EggId}."));
                return;
            }

            byte digimonSlot = (byte)Enumerable.Range(0, client.Tamer.DigimonSlots)
                .FirstOrDefault(slot => client.Tamer.Digimons.All(x => x.Slot != slot));

            // ---------- Size por hatch level (centésimos) ----------
            // 3/5 → 7500–10000 | 4/5 → 10000–11700 | 5/5 → 11800–13000
            int hatchLevel = client.Tamer.Incubator.HatchLevel; // 1..5

            static short RandomInclusiveShort(int min, int max)
            {
                // UtilitiesFunctions.RandomDouble() retorna [0,100); normalizamos para [0,1).
                double d = UtilitiesFunctions.RandomDouble();
                double r01 = d > 1.0 ? d / 100.0 : d;        // se já for [0,1), mantém
                if (r01 >= 1.0) r01 = 0.9999999;            // evita cair em max+1 após Floor
                int val = min + (int)Math.Floor(r01 * (max - min + 1));
                if (val < min) val = min;
                if (val > max) val = max;
                return (short)val;
            }

            short size = hatchLevel switch
            {
                3 => RandomInclusiveShort(7500, 10000),   // 75,00% – 100,00%
                4 => RandomInclusiveShort(10000, 11700),  // 100,00% – 117,00%
                5 => RandomInclusiveShort(11800, 13000),  // 118,00% – 130,00%
                _ => client.Tamer.Incubator.GetLevelSize() // 1/5 e 2/5: comportamento original
            };

            var newDigimon = DigimonModel.Create(
                digiName,
                hatchInfo.HatchType,
                hatchInfo.HatchType,
                (DigimonHatchGradeEnum)hatchLevel,
                size,
                digimonSlot
            );

            newDigimon.NewLocation(client.Tamer.Location.MapId, client.Tamer.Location.X, client.Tamer.Location.Y);

            newDigimon.SetBaseInfo(_statusManager.GetDigimonBaseInfo(newDigimon.BaseType));
            newDigimon.SetBaseStatus(_statusManager.GetDigimonBaseStatus(newDigimon.BaseType, newDigimon.Level, newDigimon.Size));

            if (newDigimon.BaseInfo == null || newDigimon.BaseStatus == null)
            {
                _logger.Warning($"Invalid base data for Digimon {newDigimon.BaseType}.");
                client.Send(new SystemMessagePacket($"Invalid base data for Digimon {newDigimon.BaseType}."));
                return;
            }

            var digimonEvolutionInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == newDigimon.BaseType);
            if (digimonEvolutionInfo == null)
            {
                _logger.Warning($"No evolution info for Digimon {newDigimon.BaseType}.");
                client.Send(new SystemMessagePacket($"No evolution info for Digimon {newDigimon.BaseType}."));
                return;
            }

            newDigimon.AddEvolutions(digimonEvolutionInfo);
            if (!newDigimon.Evolutions.Any())
            {
                _logger.Warning($"No evolutions found for Digimon {newDigimon.BaseType}.");
                client.Send(new SystemMessagePacket($"No evolutions found for Digimon {newDigimon.BaseType}."));
                return;
            }

            newDigimon.SetTamer(client.Tamer);

            // Adiciona o Digimon ao Tamer imediatamente para evitar atrasos
            client.Tamer.AddDigimon(newDigimon);

            if (client.Tamer.Incubator.PerfectSize(newDigimon.HatchGrade, newDigimon.Size))
            {
                client.SendToAll(new NeonMessagePacket(NeonMessageTypeEnum.Scale, client.Tamer.Name,
                    newDigimon.BaseType, newDigimon.Size).Serialize());
            }

            var digimonInfo = _mapper.Map<DigimonModel>(await _sender.Send(new CreateDigimonCommand(newDigimon)));
            client.Tamer.Incubator.RemoveEgg();
            await _sender.Send(new UpdateIncubatorCommand(client.Tamer.Incubator));

            client.Send(new HatchFinishPacket(newDigimon, (ushort)(client.Partner.GeneralHandler + 1000), digimonSlot));

            if (digimonInfo != null)
            {
                newDigimon.SetId(digimonInfo.Id);
                for (int i = 0; i < newDigimon.Evolutions.Count; i++)
                {
                    var evolution = digimonInfo.Evolutions.ElementAtOrDefault(i);
                    if (evolution != null)
                    {
                        newDigimon.Evolutions[i].SetId(evolution.Id);
                        for (int j = 0; j < newDigimon.Evolutions[i].Skills.Count; j++)
                        {
                            var skill = evolution.Skills.ElementAtOrDefault(j);
                            if (skill != null)
                            {
                                newDigimon.Evolutions[i].Skills[j].SetId(skill.Id);
                            }
                        }
                    }
                }
            }

            _logger.Verbose(
                $"Character {client.TamerId} hatched {newDigimon.Id}({newDigimon.BaseType}) with grade {newDigimon.HatchGrade} and size {newDigimon.Size}.");

            // Atualiza a enciclopédia
            if (!client.Tamer.Encyclopedia.Exists(x => x.DigimonEvolutionId == digimonEvolutionInfo.Id))
            {
                var encyclopedia = CharacterEncyclopediaModel.Create(client.TamerId, digimonEvolutionInfo.Id,
                    newDigimon.Level, newDigimon.Size, 0, 0, 0, 0, 0, false, false);

                digimonEvolutionInfo.Lines.ForEach(line =>
                {
                    encyclopedia.Evolutions.Add(CharacterEncyclopediaEvolutionsModel.Create(encyclopedia.Id, line.Type,
                        line.SlotLevel, false));
                });

                var encyclopediaAdded = await _sender.Send(new CreateCharacterEncyclopediaCommand(encyclopedia));
                client.Tamer.Encyclopedia.Add(encyclopediaAdded);
            }
        }
    }
}
