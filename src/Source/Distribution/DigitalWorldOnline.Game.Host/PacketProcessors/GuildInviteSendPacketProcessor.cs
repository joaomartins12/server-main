using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class GuildInviteSendPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.GuildInvite;

        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public GuildInviteSendPacketProcessor(MapServer mapServer, DungeonsServer dungeonsServer, EventServer eventServer, PvpServer pvpServer, ILogger logger, ISender sender)
        {
            _mapServer = mapServer;
            _dungeonsServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            if (client.Tamer.Guild != null)
            {
                var packet = new GamePacketReader(packetData);

                var targetName = packet.ReadString();

                var targetClient = _mapServer.FindClientByTamerName(targetName) ??
                                   _dungeonsServer.FindClientByTamerName(targetName) ??
                                   _pvpServer.FindClientByTamerName(targetName) ??
                                   _eventServer.FindClientByTamerName(targetName);

                if (targetClient == null)
                {
                    _logger.Warning($"Character {client.Tamer.Name} sent guild invite to {targetName} but {targetName} was not found !!");

                    client.Send(new GuildInviteFailPacket(GuildInviteFailEnum.TargetNotConnected, targetName));
                }
                else if (targetClient.Tamer.State != CharacterStateEnum.Ready)
                {
                    _logger.Warning($"Character {client.Tamer.Name} sent guild invite to {targetClient.TamerId} {targetName} which was not connected.");
                    _logger.Warning($"Character {targetName} state = {targetClient.Tamer.State}");

                    client.Send(new GuildInviteFailPacket(GuildInviteFailEnum.TargetNotConnected, targetName));
                }
                else
                {
                    _logger.Debug($"Searching if character {targetClient.TamerId} - {targetClient.Tamer.AccountId} have guild !!");
                    var targetGuild = await _sender.Send(new GuildByCharacterIdQuery(targetClient.TamerId));

                    if (targetGuild != null)
                    {
                        _logger.Warning($"Character {client.Tamer.Name} sent guild invite to {targetClient.Tamer.Name} which was in another guild.");

                        client.Send(new GuildInviteFailPacket(GuildInviteFailEnum.TargetInAnotherGuild, targetName));
                    }
                    else
                    {
                        var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(targetClient.Tamer.Location.MapId));

                        switch (mapConfig?.Type)
                        {
                            case MapTypeEnum.Dungeon:
                                _dungeonsServer.BroadcastForUniqueTamer(targetClient.TamerId, new GuildInviteSuccessPacket(client.Tamer, targetClient.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Event:
                                _eventServer.BroadcastForUniqueTamer(targetClient.TamerId, new GuildInviteSuccessPacket(client.Tamer, targetClient.Tamer).Serialize());
                                break;

                            case MapTypeEnum.Pvp:
                                _pvpServer.BroadcastForUniqueTamer(targetClient.TamerId, new GuildInviteSuccessPacket(client.Tamer, targetClient.Tamer).Serialize());
                                break;

                            default:
                                {
                                    _mapServer.BroadcastForUniqueTamer(targetClient.TamerId, new GuildInviteSuccessPacket(client.Tamer, targetClient.Tamer).Serialize());
                                }
                                break;
                        }
                    }
                }
            }
        }
    }
}
