using AutoMapper;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Mechanics;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class GuildCreatePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.CreateGuild;

        private readonly MapServer _mapServer;
        private readonly EventServer _eventServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;

        public GuildCreatePacketProcessor(
            MapServer mapServer,
            EventServer eventServer,
            DungeonsServer dungeonsServer,
            PvpServer pvpServer,
            ISender sender,
            ILogger logger,
            IMapper mapper
        )
        {
            _mapServer = mapServer;
            _eventServer = eventServer;
            _dungeonsServer = dungeonsServer;
            _pvpServer = pvpServer;
            _sender = sender;
            _logger = logger;
            _mapper = mapper;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            try
            {
                var packet = new GamePacketReader(packetData);

                var guildName = packet.ReadString();
                packet.Skip(1);
                var itemSlot = packet.ReadShort();
                var npcId = packet.ReadInt();

                _logger.Information($"Guild creation request: GuildName={guildName}, ItemSlot={itemSlot}, NpcId={npcId}");

                // Validações anteriores
                if (client.Tamer.Guild != null)
                {
                    _logger.Warning($"Player {client.Tamer.Name} is already in a guild.");
                    client.Send(new GuildCreateFailPacket(client.Tamer.Name, guildName));
                    return;
                }

                var nameTaken = await _sender.Send(new GuildByGuildNameQuery(guildName)) != null;
                var guildPermit = client.Tamer.Inventory.FindItemBySlot(itemSlot);

                if (guildPermit == null || guildPermit.Amount <= 0 || nameTaken)
                {
                    _logger.Warning($"Guild creation failed for {client.Tamer.Name}: Invalid conditions.");
                    client.Send(new GuildCreateFailPacket(client.Tamer.Name, guildName));
                    return;
                }

                // Crie a guilda
                var guild = GuildModel.Create(guildName);
                guild.AddMember(client.Tamer, GuildAuthorityTypeEnum.Master);
                guild.AddHistoricEntry(GuildHistoricTypeEnum.GuildCreate, guild.Master, guild.Master);

                client.Tamer.SetGuild(guild);
                await _sender.Send(new CreateGuildCommand(guild));

                // Notificar o cliente
                _logger.Debug($"Sending guild create success packet for character {client.TamerId}...");
                client.Send(new GuildCreateSuccessPacket(client.Tamer.Name, itemSlot, guildName));

                _logger.Debug($"Sending guild information packet for character {client.TamerId}...");
                client.Send(new GuildInformationPacket(guild));

                _logger.Debug($"Sending guild historic packet for character {client.TamerId}...");
                client.Send(new GuildHistoricPacket(client.Tamer.Guild!.Historic));

                // Obtenha a classificação da guilda
                _logger.Debug($"Getting guild rank position for guild {client.Tamer.Guild.Id}...");
                var guildRank = await _sender.Send(new GuildCurrentRankByGuildIdQuery(client.Tamer.Guild.Id));

                if (guildRank > 0 && guildRank <= 100)
                {
                    _logger.Debug($"Sending guild rank packet for character {client.TamerId}...");
                    client.Send(new GuildRankPacket(guildRank));
                }

                // Atualizar a visibilidade do jogador
                UpdatePlayerVisibility(client);

                // Consumir el objeto necesario
                _logger.Debug($"Consuming guild permit item for character {client.TamerId}...");
                client.Tamer.Inventory.RemoveOrReduceItem(guildPermit, 1);
                await _sender.Send(new UpdateItemCommand(guildPermit));

                _logger.Information($"Guild {guildName} created successfully by {client.Tamer.Name}.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during guild creation for {client?.Tamer?.Name}: {ex.Message}");
                client?.Send(new GuildCreateFailPacket(client?.Tamer?.Name ?? "Unknown", "Unknown"));
            }
        }

        private void UpdatePlayerVisibility(GameClient client)
        {
            try
            {
                var packets = UtilitiesFunctions.GroupPackets(
                    new UnloadTamerPacket(client.Tamer).Serialize(),
                    new LoadTamerPacket(client.Tamer).Serialize(),
                    new LoadBuffsPacket(client.Tamer).Serialize()
                );

                // Transmitir para todos os servidores relevantes
                _mapServer.BroadcastForTargetTamers(client.TamerId, packets);
                _pvpServer.BroadcastForTargetTamers(client.TamerId, packets);
                _eventServer.BroadcastForTargetTamers(client.TamerId, packets);
                _dungeonsServer.BroadcastForTargetTamers(client.TamerId, packets);

                _logger.Debug($"Player visibility updated for {client.Tamer.Name} across all servers.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error updating player visibility for {client?.Tamer?.Name}: {ex.Message}");
            }
        }

        private void ReloadPlayer(GameClient client)
        {
            try
            {
                var unloadPacket = new UnloadTamerPacket(client.Tamer).Serialize();
                var loadPacket = new LoadTamerPacket(client.Tamer).Serialize();
                var buffsPacket = new LoadBuffsPacket(client.Tamer).Serialize();

                var packets = UtilitiesFunctions.GroupPackets(unloadPacket, loadPacket, buffsPacket);

                _mapServer.BroadcastForTargetTamers(client.TamerId, packets);
                _pvpServer.BroadcastForTargetTamers(client.TamerId, packets);
                _eventServer.BroadcastForTargetTamers(client.TamerId, packets);
                _dungeonsServer.BroadcastForTargetTamers(client.TamerId, packets);

                _logger.Information($"Player {client.Tamer.Name} reloaded successfully after exiting dungeon.");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error reloading player {client?.Tamer?.Name}: {ex.Message}");
            }
        }
    }
}