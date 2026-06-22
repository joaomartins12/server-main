using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Game.Utils
{
    public static class WarpHelper
    {
        private static readonly ConcurrentDictionary<long, long> _inFlight = new();

        public static async Task<bool> TryWarpAsync(
            string tag,               // ex.: "RoundPortal"
            GameClient c,
            int destMapId, int destX, int destY,
            MapServer mapServer, DungeonsServer dungeonsServer, EventServer eventServer, PvpServer pvpServer,
            ISender sender, ILogger logger, IConfiguration cfg,
            int resendTries = 4,      // nº de reenvios após o 1º
            int resendIntervalMs = 900
        )
        {
            // Anti-reentrada
            var now = Environment.TickCount64;
            if (_inFlight.TryGetValue(c.TamerId, out var last) && now - last < 800)
            {
                logger.Warning("[{Tag}][Guard] duplicate warp ignored tamer={TamerId}", tag, c.TamerId);
                return false;
            }
            _inFlight[c.TamerId] = now;

            try
            {
                logger.Information("[{Tag}][Enter] tamer={TamerId} currMap={Map} pos=({X},{Y}) -> dest={{map={DMap},x={DX},y={DY}}}",
                    tag, c.TamerId, c.Tamer.Location.MapId, c.Tamer.Location.X, c.Tamer.Location.Y, destMapId, destX, destY);

                // Local (mesmo mapa) => LocalMapSwap
                if (destMapId == c.Tamer.Location.MapId)
                {
                    c.Tamer.NewLocation(destMapId, destX, destY);
                    c.Tamer.Partner.NewLocation(destMapId, destX, destY);

                    try
                    {
                        c.Send(new LocalMapSwapPacket(c.Tamer.GeneralHandler, c.Tamer.Partner.GeneralHandler, destX, destY, destX, destY));
                        logger.Information("[{Tag}][Local] tamer={TamerId} -> ({X},{Y})", tag, c.TamerId, destX, destY);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "[{Tag}][Local] LocalMapSwap send failed tamer={TamerId}", tag, c.TamerId);
                    }

                    // Persist best-effort
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await sender.Send(new UpdateCharacterLocationCommand(c.Tamer.Location));
                            await sender.Send(new UpdateDigimonLocationCommand(c.Tamer.Partner.Location));
                        }
                        catch (Exception ex)
                        {
                            logger.Warning(ex, "[{Tag}][Local][Persist] failed tamer={TamerId}", tag, c.TamerId);
                        }
                    });

                    return true;
                }

                // CROSS MAP (ordem importante: MapSwap -> set Loading/persist -> remoções)
                c.Tamer.NewLocation(destMapId, destX, destY);
                c.Tamer.Partner.NewLocation(destMapId, destX, destY);

                var pub = cfg["GameServer:PublicAddress"];
                var port = cfg["GameServer:Port"];

                try
                {
                    c.Send(new MapSwapPacket(pub, port, c.Tamer.Location.MapId, c.Tamer.Location.X, c.Tamer.Location.Y));
                    logger.Information("[{Tag}][Cross][MapSwap send] try=1 tamer={TamerId} -> map={Map} ({X},{Y})",
                        tag, c.TamerId, destMapId, destX, destY);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "[{Tag}][Cross] MapSwap first send failed tamer={TamerId}", tag, c.TamerId);
                }

                // Agora marca Loading e persiste
                c.Tamer.UpdateState(CharacterStateEnum.Loading);
                c.SetGameQuit(false);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await sender.Send(new UpdateCharacterLocationCommand(c.Tamer.Location));
                        await sender.Send(new UpdateDigimonLocationCommand(c.Tamer.Partner.Location));
                        await sender.Send(new UpdateCharacterStateCommand(c.TamerId, CharacterStateEnum.Loading));
                    }
                    catch (Exception ex)
                    {
                        logger.Warning(ex, "[{Tag}][Cross][Persist] failed tamer={TamerId}", tag, c.TamerId);
                    }

                    // pequena espera antes de remover do servidor atual

                    try
                    {
                        if (c.DungeonMap) dungeonsServer.RemoveClient(c);
                        else mapServer.RemoveClient(c);
                        eventServer?.RemoveClient(c);
                        pvpServer?.RemoveClient(c);
                    }
                    catch (Exception ex)
                    {
                        logger.Warning(ex, "[{Tag}][Cross][RemoveClient] deferred failed tamer={TamerId}", tag, c.TamerId);
                    }
                });

                // Watchdog para clientes com latência/packetloss
                _ = RunMapSwapWatchdog(tag, c, destMapId, destX, destY, pub, port, sender, logger, resendTries, resendIntervalMs);

                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[{Tag}][Error] unexpected error tamer={TamerId}", tag, c.TamerId);
                return false;
            }
            finally
            {
                _inFlight.TryRemove(c.TamerId, out _);
            }
        }

        private static async Task RunMapSwapWatchdog(
            string tag, GameClient c, int targetMap, int x, int y,
            string pub, string port, ISender sender, ILogger logger,
            int tries, int intervalMs)
        {
            try
            {
                for (int i = 2; i <= (1 + tries); i++)
                {
                    await Task.Delay(intervalMs);
                    if (!c.IsConnected || !c.Loading)
                        return;

                    logger.Warning("[{Tag}][Watchdog] resend MapSwap try={Try} tamer={TamerId}", tag, i, c.TamerId);
                    c.Send(new MapSwapPacket(pub, port, c.Tamer.Location.MapId, c.Tamer.Location.X, c.Tamer.Location.Y));
                }

                // Ainda travado? Resgata com LocalMapSwap para ponto seguro
                await Task.Delay(300);
                if (c.IsConnected && c.Loading)
                {
                    logger.Error("[{Tag}][Rescue] softlock detected. Unsticking tamer={TamerId}", tag, c.TamerId);

                    var (sx, sy) = await GetFirstRegionOrZero(sender, targetMap);
                    if (sx == 0 && sy == 0)
                    {
                        // fallback para o mapa que está em memória
                        (sx, sy) = await GetFirstRegionOrZero(sender, c.Tamer.Location.MapId);
                    }

                    // Usa o estado "idle" do teu enum. Na maioria dos projetos é o valor 0.
                    const CharacterStateEnum IdleState = (CharacterStateEnum)0;
                    c.Tamer.UpdateState(IdleState);

                    try
                    {
                        c.Tamer.NewLocation(c.Tamer.Location.MapId, sx, sy);
                        c.Tamer.Partner.NewLocation(c.Tamer.Location.MapId, sx, sy);
                        c.Send(new LocalMapSwapPacket(c.Tamer.GeneralHandler, c.Tamer.Partner.GeneralHandler, sx, sy, sx, sy));
                        c.Send(new SystemMessagePacket("Recovered from portal issue. You were moved to a safe point."));
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "[{Tag}][Rescue] LocalMapSwap failed tamer={TamerId}", tag, c.TamerId);
                    }

                    // persist best-effort
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await sender.Send(new UpdateCharacterLocationCommand(c.Tamer.Location));
                            await sender.Send(new UpdateDigimonLocationCommand(c.Tamer.Partner.Location));
                            await sender.Send(new UpdateCharacterStateCommand(c.TamerId, IdleState));
                        }
                        catch (Exception ex)
                        {
                            logger.Warning(ex, "[{Tag}][Rescue][Persist] failed tamer={TamerId}", tag, c.TamerId);
                        }
                    });
                }
            }
            catch { /* best-effort */ }
        }

        private static async Task<(int x, int y)> GetFirstRegionOrZero(ISender sender, int mapId)
        {
            var mapConfig = await sender.Send(new GameMapConfigByMapIdQuery(mapId));
            var waypoints = await sender.Send(new MapRegionListAssetsByMapIdQuery(mapId));

            if (mapConfig == null || waypoints == null || !waypoints.Regions.Any())
                return (0, 0);

            var first = waypoints.Regions.First();
            return (first.X, first.Y);
        }
    }
}
