using AutoMapper;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Mechanics;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class GuildAuthorityChangeMasterPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.GuildAuthorityChangeToMaster;

        private readonly MapServer _mapServer;
        private readonly EventServer _eventServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IMapper _mapper;

        public GuildAuthorityChangeMasterPacketProcessor(
            MapServer mapServer,
            EventServer eventServer,
            DungeonsServer dungeonsServer,
            PvpServer pvpServer,
            ILogger logger,
            ISender sender,
            IMapper mapper)
        {
            _mapServer = mapServer;
            _eventServer = eventServer;
            _dungeonsServer = dungeonsServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var targetName = packet.ReadString();

            // Busca de cliente Online
            var targetClient = _mapServer.FindClientByTamerName(targetName) ??
                               _dungeonsServer.FindClientByTamerName(targetName) ??
                               _pvpServer.FindClientByTamerName(targetName) ??
                               _eventServer.FindClientByTamerName(targetName);

            if (targetClient == null)
            {
                _logger.Warning($"Tamer {targetName} offline!!");
                return;
            }

            // Busca de cliente Offline
            var targetCharacter = await _sender.Send(new CharacterByNameQuery(targetName));

            if (targetCharacter == null)
            {
                _logger.Warning($"Tamer {targetName} not found !!");
                client.Send(new SystemMessagePacket($"Character not found with name {targetName}."));
                return;
            }

            //var targetGuild = _mapper.Map<GuildModel>(await _sender.Send(new GuildByCharacterIdQuery(targetClient.TamerId)));
            var targetGuild = _mapper.Map<GuildModel>(await _sender.Send(new GuildByCharacterIdQuery(targetCharacter.Id)));

            if (targetGuild == null)
            {
                _logger.Error($"Tamer {targetName} does not belong to a guild.");
                client.Send(new SystemMessagePacket($"[ERROR] :: Tamer {targetName} does not belong to a guild.", ""));
                return;
            }

            foreach (var guildMember in targetGuild.Members)
            {
                if (guildMember.CharacterInfo == null)
                {
                    var guildMemberClient = _mapServer.FindClientByTamerId(guildMember.CharacterId) ?? 
                        _dungeonsServer.FindClientByTamerId(guildMember.CharacterId) ?? 
                        _pvpServer.FindClientByTamerId(guildMember.CharacterId) ?? 
                        _eventServer.FindClientByTamerId(guildMember.CharacterId);

                    if (guildMemberClient != null)
                    {
                        guildMember.SetCharacterInfo(guildMemberClient.Tamer);
                    }
                    else
                    {
                        guildMember.SetCharacterInfo(
                            _mapper.Map<CharacterModel>(await _sender.Send(new CharacterByIdQuery(guildMember.CharacterId))));
                    }
                }
            }

            var targetMember = targetGuild.FindMember(targetCharacter.Id);

            if (targetMember != null)
            {
                var newAuthority = GuildAuthorityTypeEnum.Master;

                var currentMaster = targetGuild.Members.FirstOrDefault(m => m.Authority == GuildAuthorityTypeEnum.Master);

                if (currentMaster != null)
                {
                    currentMaster.SetAuthority(GuildAuthorityTypeEnum.Member);

                    await _sender.Send(new UpdateGuildMemberAuthorityCommand(currentMaster));
                }

                targetMember.SetAuthority(newAuthority);

                var newEntry = targetGuild.AddHistoricEntry((GuildHistoricTypeEnum)newAuthority, targetGuild.Master, targetMember);
                
                targetGuild.Members
                    .ForEach(guildMember =>
                    {
                        _logger.Debug($"Sending guild historic packet for character {guildMember.CharacterId}...");

                        _mapServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildHistoricPacket(targetGuild.Historic).Serialize());
                        _dungeonsServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildHistoricPacket(targetGuild.Historic).Serialize());
                        _eventServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildHistoricPacket(targetGuild.Historic).Serialize());
                        _pvpServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildHistoricPacket(targetGuild.Historic).Serialize());

                        _logger.Debug($"Sending guild authority change packet for character {guildMember.CharacterId}...");

                        _mapServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildPromotionDemotionPacket(packet.Type, targetName,
                                targetGuild.FindAuthority(newAuthority).Duty).Serialize());
                        _dungeonsServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildPromotionDemotionPacket(packet.Type, targetName,
                                targetGuild.FindAuthority(newAuthority).Duty).Serialize());
                        _eventServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildPromotionDemotionPacket(packet.Type, targetName,
                                targetGuild.FindAuthority(newAuthority).Duty).Serialize());
                        _pvpServer.BroadcastForUniqueTamer(guildMember.CharacterId,
                            new GuildPromotionDemotionPacket(packet.Type, targetName,
                                targetGuild.FindAuthority(newAuthority).Duty).Serialize());
                    });

                _logger.Debug($"Saving historic entry for guild {targetGuild.Id}...");
                await _sender.Send(new CreateGuildHistoricEntryCommand(newEntry, targetGuild.Id));

                _logger.Debug($"Updating member authority for member {targetMember.Id} and guild {targetGuild.Id}...");
                await _sender.Send(new UpdateGuildMemberAuthorityCommand(targetMember));
            }
        }
    }
}