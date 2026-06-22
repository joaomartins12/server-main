using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Models.TamerShop;
using DigitalWorldOnline.Commons.Packets.PersonalShop;
using DigitalWorldOnline.Commons.Utils;

namespace DigitalWorldOnline.GameHost
{
    public sealed partial class MapServer
    {
        // ---- Throttle por Tamer ----
        private readonly Dictionary<long, DateTime> _lastShopSyncAt = new();
        private static readonly TimeSpan _shopSyncInterval = TimeSpan.FromMilliseconds(250);

        // ---- Atualização periódica ----
        private static readonly TimeSpan _shopUpdateInterval = TimeSpan.FromSeconds(5);
        private DateTime _lastGlobalShopUpdate = DateTime.MinValue;

        // ---- Visibilidade de Consigned Shops ----
        private void ShowOrHideConsignedShop(GameMap map, CharacterModel tamer)
        {
            var now = DateTime.UtcNow;

            // Throttle leve por tamer
            if (_lastShopSyncAt.TryGetValue(tamer.Id, out var last) && (now - last) < _shopSyncInterval)
                return;
            _lastShopSyncAt[tamer.Id] = now;

            // Snapshot atual das lojas do mapa
            var shopsSnapshot = new List<ConsignedShop>(map.ConsignedShops);

            _logger.Debug($"[ConsignedShop] Snapshot={shopsSnapshot.Count} shops no mapa {map.Id} (canal {tamer.Channel}) para tamer {tamer.Id}:{tamer.Name} -> {string.Join(",", shopsSnapshot.Select(s => $"{s.Id}:{s.Channel}"))}");

            // Mostra apenas as que o cliente ainda não vê
            foreach (var shop in shopsSnapshot)
            {
                // ⚠️ Verificar se canal está a cortar as shops
                if (shop.Channel != tamer.Channel)
                {
                    _logger.Debug($"[ConsignedShop] Ignorada shop {shop.Id}:{shop.ShopName} (canal {shop.Channel}) pois tamer está no canal {tamer.Channel}");
                    continue;
                }

                if (!map.ViewingConsignedShop(shop.Id, tamer.Id))
                {
                    ShowConsignedShop(map, shop, tamer.Id);
                }
            }

            // 🚫 Não escondemos nada automaticamente
        }

        private void ShowConsignedShop(GameMap map, ConsignedShop shopToShow, long tamerToSeeId)
        {
            // garante que o estado interno do map fica marcado
            if (!map.ViewingConsignedShop(shopToShow.Id, tamerToSeeId))
                map.ShowConsignedShop(shopToShow.Id, tamerToSeeId);

            // ✅ reenvia SEMPRE o pacote para o cliente (idempotente do lado do client)
            var targetClient = map.Clients.FirstOrDefault(x => x.TamerId == tamerToSeeId);
            if (targetClient != null)
            {
                _logger.Debug($"[ConsignedShop] Enviando shop {shopToShow.Id}:{shopToShow.ShopName} para tamer {tamerToSeeId}...");
                targetClient.Send(new LoadConsignedShopPacket(shopToShow).Serialize());
            }
        }

        private void HideConsignedShop(GameMap map, ConsignedShop shopToHide, long tamerToBlindId)
        {
            if (map.ViewingConsignedShop(shopToHide.Id, tamerToBlindId))
            {
                _logger.Debug($"[ConsignedShop] Hiding consigned shop {shopToHide.Id} - {shopToHide.ShopName} for tamer {tamerToBlindId}...");
                map.HideConsignedShop(shopToHide.Id, tamerToBlindId);

                var targetClient = map.Clients.FirstOrDefault(x => x.TamerId == tamerToBlindId);
                targetClient?.Send(new UnloadConsignedShopPacket(shopToHide).Serialize());
            }
        }

        private void HideConsignedShopById(GameMap map, long shopId, long tamerToBlindId)
        {
            if (map.ViewingConsignedShop(shopId, tamerToBlindId))
            {
                _logger.Debug($"[ConsignedShop] Hiding consigned shop {shopId} for tamer {tamerToBlindId}...");
                map.HideConsignedShop(shopId, tamerToBlindId);

                var targetClient = map.Clients.FirstOrDefault(x => x.TamerId == tamerToBlindId);
                targetClient?.Send(new UnloadConsignedShopPacket(shopId).Serialize());
            }
        }

        // ⚡ Limpa o throttle para esse tamer
        public void ResetConsignedShopsForTamer(GameMap map, CharacterModel tamer)
        {
            _lastShopSyncAt.Remove(tamer.Id);

            // 🔑 Limpa também as shops que o mapa ainda acha que o tamer vê
            var viewingShops = map.ConsignedShops.ToList();
            foreach (var shop in viewingShops)
            {
                if (map.ViewingConsignedShop(shop.Id, tamer.Id))
                {
                    map.HideConsignedShop(shop.Id, tamer.Id);
                }
            }

            _logger.Information($"[ConsignedShop] Reset shops para tamer {tamer.Id}:{tamer.Name} no mapa {map.Id} ch {tamer.Channel}.");
        }

        // ⚡ Força um resync imediato das lojas visíveis para esse tamer
        public void ForceShopsync(GameMap map, CharacterModel tamer)
        {
            _logger.Debug($"[ConsignedShop] Force shop sync for tamer {tamer.Id}:{tamer.Name} on map {map.Id} ch {tamer.Channel}.");

            // força passar pelo throttle
            _lastShopSyncAt[tamer.Id] = DateTime.UtcNow - (_shopSyncInterval + TimeSpan.FromMilliseconds(1));
            ShowOrHideConsignedShop(map, tamer);

            // 🔄 Reforço de segurança: agenda novo sync em 3s
            Task.Run(async () =>
            {
                await Task.Delay(3000);
                try
                {
                    _lastShopSyncAt[tamer.Id] = DateTime.UtcNow - (_shopSyncInterval + TimeSpan.FromMilliseconds(1));
                    ShowOrHideConsignedShop(map, tamer);
                    _logger.Debug($"[ConsignedShop] Resync (3s delay) concluído para tamer {tamer.Id}:{tamer.Name}.");
                }
                catch (Exception ex)
                {
                    _logger.Error($"[ConsignedShop] Falha ao fazer resync atrasado para {tamer.Id}:{tamer.Name}: {ex.Message}");
                }
            });
            _logger.Information($"[SHOP DEBUG] A enviar {map.ConsignedShops.Count} shops para {tamer.Name}");
        }

        // 🚪 Chamado quando o player entra no mapa
        public void OnTamerEnterMap(GameMap map, CharacterModel tamer)
        {
            ResetConsignedShopsForTamer(map, tamer);

            // ⚡ Força envio imediato de todas as shops
            foreach (var shop in map.ConsignedShops)
            {
                ShowConsignedShop(map, shop, tamer.Id);
            }

            _logger.Information($"[SHOP DEBUG] {map.ConsignedShops.Count} shops enviadas imediatamente para {tamer.Name} ao entrar no mapa {map.Id}.");
        }

        // 🔄 Chamado quando o player dá reload no mesmo mapa
        public void OnTamerReload(GameMap map, CharacterModel tamer)
        {
            ResetConsignedShopsForTamer(map, tamer);

            // ⚡ Força envio imediato de todas as shops
            foreach (var shop in map.ConsignedShops)
            {
                ShowConsignedShop(map, shop, tamer.Id);
            }

            _logger.Information($"[SHOP DEBUG] {map.ConsignedShops.Count} shops enviadas imediatamente para {tamer.Name} ao entrar no mapa {map.Id}.");
        }

        // 🌍 Atualiza periodicamente todas as shops visíveis no mapa
        private void UpdateConsignedShops(GameMap map)
        {
            var now = DateTime.UtcNow;

            if ((now - _lastGlobalShopUpdate) < _shopUpdateInterval)
                return;

            _lastGlobalShopUpdate = now;

            foreach (var client in map.Clients)
            {
                var tamer = client.Tamer;
                if (tamer == null) continue;

                try
                {
                    ShowOrHideConsignedShop(map, tamer);
                }
                catch (Exception ex)
                {
                    _logger.Error($"[ConsignedShop] Erro ao atualizar shops para tamer {tamer.Id}:{tamer.Name}: {ex.Message}");
                }
            }
        }

        // 🔔 Chamado dentro do loop de update do mapa
        public void UpdateMap(GameMap map)
        {
            // ... outras atualizações do mapa (mobs, quests, etc.)

            UpdateConsignedShops(map); // 🔑 agora as shops sincronizam de 5 em 5 segundos
        }
    }
}
