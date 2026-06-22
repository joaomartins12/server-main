using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using System.Collections.Concurrent;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class MovimentationPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.TamerMovimentation;

        private readonly PartyManager _partyManager;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;

        private static readonly ConcurrentDictionary<int, DateTime> _lastUpdateSent = new();

        public MovimentationPacketProcessor(PartyManager partyManager, MapServer mapServer, DungeonsServer dungeonServer,
            EventServer eventServer, PvpServer pvpServer, ISender sender)
        {
            _partyManager = partyManager;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var ticks = packet.ReadUInt();
            var handler = packet.ReadUInt();
            var newX = packet.ReadInt();
            var newY = packet.ReadInt();
            var newZ = packet.ReadFloat();

            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            if (client.Tamer.PreviousCondition == ConditionEnum.Ride && client.Tamer.CurrentCondition == ConditionEnum.Away)
            {
                client.Tamer.ResetAfkNotifications();
                client.Tamer.UpdateCurrentCondition(ConditionEnum.Ride);
                BroadcastCondition(mapConfig?.Type, (int)client.TamerId, client.Tamer.CurrentCondition, true);
            }

            if (client.Tamer.Riding)
            {
                client.Tamer.NewLocation(newX, newY, newZ);
                client.Tamer.Partner.NewLocation(newX, newY, newZ);
                BroadcastMovimentPacket(mapConfig?.Type, (int)client.TamerId, new TamerWalkPacket(client.Tamer), new DigimonWalkPacket(client.Tamer.Partner));
            }
            else
            {
                if (client.Tamer.CurrentCondition == ConditionEnum.Away)
                {
                    client.Tamer.ResetAfkNotifications();
                    client.Tamer.UpdateCurrentCondition(ConditionEnum.Default);
                    BroadcastCondition(mapConfig?.Type, (int)client.TamerId, client.Tamer.CurrentCondition, false);
                }

                if (handler >= short.MaxValue)
                {
                    client.Tamer.NewLocation(newX, newY, newZ);
                    BroadcastMovimentPacket(mapConfig?.Type, (int)client.TamerId, new TamerWalkPacket(client.Tamer));
                }
                else
                {
                    client.Tamer.Partner.NewLocation(newX, newY, newZ);
                    BroadcastMovimentPacket(mapConfig?.Type, (int)client.TamerId, null, new DigimonWalkPacket(client.Tamer.Partner));
                }
            }

            var party = _partyManager.FindParty(client.TamerId);
            if (party != null)
            {
                party.UpdateMember(party[client.TamerId], client.Tamer);

                var movementPacket = new PartyMemberMovimentationPacket(party[client.TamerId]).Serialize();

                foreach (var member in party.Members.Values.Where(x => x.Id != client.TamerId))
                {
                    _mapServer.BroadcastForUniqueTamer(member.Id, movementPacket);
                    _dungeonServer.BroadcastForUniqueTamer(member.Id, movementPacket);
                    _eventServer.BroadcastForUniqueTamer(member.Id, movementPacket);
                    _pvpServer.BroadcastForUniqueTamer(member.Id, movementPacket);
                }
            }

            // Aguarda 5 segundos entre envios por cliente
            var now = DateTime.UtcNow;
            if (!_lastUpdateSent.TryGetValue((int)client.TamerId, out var lastUpdate) || (now - lastUpdate).TotalSeconds >= 7)
            {
                _lastUpdateSent[(int)client.TamerId] = now;

                await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));
                await _sender.Send(new UpdateDigimonLocationCommand(client.Partner.Location));
            }
        }

        private void BroadcastCondition(MapTypeEnum? mapType, int tamerId, ConditionEnum condition, bool includeSelf)
        {
            var packet = new SyncConditionPacket(tamerId, condition).Serialize();
            switch (mapType)
            {
                case MapTypeEnum.Dungeon:
                    if (includeSelf)
                        _dungeonServer.BroadcastForTamerViewsAndSelf(tamerId, packet);
                    else
                        _dungeonServer.BroadcastForTargetTamers(tamerId, packet);
                    break;
                case MapTypeEnum.Event:
                    if (includeSelf)
                        _mapServer.BroadcastForTamerViewsAndSelf(tamerId, packet);
                    else
                        _eventServer.BroadcastForTargetTamers(tamerId, packet);
                    break;
                case MapTypeEnum.Pvp:
                    if (includeSelf)
                        _pvpServer.BroadcastForTamerViewsAndSelf(tamerId, packet);
                    else
                        _pvpServer.BroadcastForTargetTamers(tamerId, packet);
                    break;
                default:
                    if (includeSelf)
                        _mapServer.BroadcastForTamerViewsAndSelf(tamerId, packet);
                    else
                        _mapServer.BroadcastForTargetTamers(tamerId, packet);
                    break;
            }
        }

        private void BroadcastMovimentPacket(MapTypeEnum? mapType, int tamerId, TamerWalkPacket? tamerPacket = null, DigimonWalkPacket? digimonPacket = null)
        {
            // Verifica o tipo de mapa e faz o broadcast para o servidor correspondente
            switch (mapType)
            {
                case MapTypeEnum.Dungeon:
                    if (tamerPacket != null)
                        _dungeonServer.BroadcastForTargetTamers(tamerId, tamerPacket.Serialize());
                    if (digimonPacket != null)
                        _dungeonServer.BroadcastForTargetTamers(tamerId, digimonPacket.Serialize());
                    break;

                case MapTypeEnum.Event:
                    if (tamerPacket != null)
                        _eventServer.BroadcastForTargetTamers(tamerId, tamerPacket.Serialize());
                    if (digimonPacket != null)
                        _eventServer.BroadcastForTargetTamers(tamerId, digimonPacket.Serialize());
                    break;

                case MapTypeEnum.Pvp:
                    if (tamerPacket != null)
                        _pvpServer.BroadcastForTargetTamers(tamerId, tamerPacket.Serialize());
                    if (digimonPacket != null)
                        _pvpServer.BroadcastForTargetTamers(tamerId, digimonPacket.Serialize());
                    break;

                default:
                    if (tamerPacket != null)
                        _mapServer.BroadcastForTargetTamers(tamerId, tamerPacket.Serialize());
                    if (digimonPacket != null)
                        _mapServer.BroadcastForTargetTamers(tamerId, digimonPacket.Serialize());
                    break;
            }
        }
    }
}
