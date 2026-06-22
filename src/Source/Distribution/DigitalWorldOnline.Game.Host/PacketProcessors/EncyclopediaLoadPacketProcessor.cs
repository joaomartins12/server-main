using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class EncyclopediaLoadPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.EncyclopediaLoad;

        private readonly ILogger _logger;

        public EncyclopediaLoadPacketProcessor(ILogger logger)
        {
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            try
            {
                var packet = new GamePacketReader(packetData);
                var tamer = client.Tamer;

                if (tamer == null)
                {
                    _logger.Warning("[EncyclopediaLoad] Tamer not found for client.");
                    return;
                }

                var encyclopedia = tamer.Encyclopedia;

                if (encyclopedia == null)
                {
                    _logger.Warning($"[EncyclopediaLoad] Encyclopedia data missing for CharacterId={tamer.Id}");
                    return;
                }

                // 🔁 Atualiza sempre — mesmo que já tenha sido carregada antes
                _logger.Debug($"[EncyclopediaLoad] Refreshing encyclopedia for CharacterId={tamer.Id}, Entries={encyclopedia.Count}");

                // ✅ Reenvia sempre os dados atuais ao cliente
                client.Send(new EncyclopediaLoadPacket(encyclopedia));

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.Error($"[EncyclopediaLoad] Error updating encyclopedia: {ex.Message} {ex.StackTrace}");
            }
        }
    }
}
