using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Chat;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class ChatMessagePacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.ChatMessage;

        private readonly GameMasterCommandsProcessor _gmCommands;
        private readonly PlayerCommands _playerCommands;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;

        public ChatMessagePacketProcessor(GameMasterCommandsProcessor gmCommands, PlayerCommands playerCommands,
            MapServer mapServer, DungeonsServer dungeonServer, EventServer eventServer, PvpServer pvpServer,
            ILogger logger, ISender sender)
        {
            _gmCommands = gmCommands;
            _playerCommands = playerCommands;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);
            string message = packet.ReadString();

            if (string.IsNullOrWhiteSpace(message))
                return;

            // Se for comando (!)
            if (message.StartsWith("!"))
            {
                _logger.Debug($"Tamer trys to execute \"{message}\".");

                if (client.AccessLevel <= AccountAccessLevelEnum.Vip5)
                    await _playerCommands.ExecuteCommand(client, message.TrimStart('!'));
                else
                    await _gmCommands.ExecuteCommand(client, message.TrimStart('!'));

                return;
            }

            // Agora sim busca o mapa se for necessário para chat normal
            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            var chatPacket = new ChatMessagePacket(message, ChatTypeEnum.Normal, client.Tamer.GeneralHandler).Serialize();
            var chatModel = ChatMessageModel.Create(client.TamerId, message);

            string cor, canal;
            switch (client.AccessLevel)
            {
                case AccountAccessLevelEnum.Default:
                case AccountAccessLevelEnum.Vip:
                case AccountAccessLevelEnum.Vip2:
                case AccountAccessLevelEnum.Vip3:
                case AccountAccessLevelEnum.Vip4:
                case AccountAccessLevelEnum.Vip5:
                    cor = "00ff05";
                    canal = "C";
                    break;

                case AccountAccessLevelEnum.Moderator:
                case AccountAccessLevelEnum.GameMasterOne:
                case AccountAccessLevelEnum.GameMasterTwo:
                case AccountAccessLevelEnum.GameMasterThree:
                case AccountAccessLevelEnum.Administrator:
                    cor = "6b00ff";
                    canal = "STAFF";
                    break;

                case AccountAccessLevelEnum.Blocked:
                    return;

                default:
                    _logger.Warning($"Invalid Access Level for account {client.AccountId}.");
                    return;
            }

            _logger.Debug($"Tamer says \"{message}\" to NormalChat.");

            // Call Discord (async mas não depende do resto)
            var discordTask = _mapServer.CallDiscord(message, client, cor, canal);

            // Broadcast
            switch (mapConfig?.Type)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId, chatPacket);
                    break;

                case MapTypeEnum.Event:
                    _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId, chatPacket);
                    break;

                case MapTypeEnum.Pvp:
                    _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId, chatPacket);
                    break;

                default:
                    _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId, chatPacket);
                    break;
            }

            // Salvar no banco (chat logs)
            var saveTask = _sender.Send(new CreateChatMessageCommand(chatModel));

            await Task.WhenAll(discordTask, saveTask);
        }
    }
}