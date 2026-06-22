using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class PartnerEvolutionPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.PartnerEvolution;

        // chave long para coincidir com client.TamerId
        private static readonly ConcurrentDictionary<long, SemaphoreSlim> _evoLocks = new();

        private readonly PartyManager _partyManager;
        private readonly StatusManager _statusManager;
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ISender _sender;
        private readonly ILogger _logger;

        public PartnerEvolutionPacketProcessor(PartyManager partyManager, StatusManager statusManager,
            AssetsLoader assets,
            MapServer mapServer, DungeonsServer dungeonServer, EventServer eventServer, PvpServer pvpServer,
            ISender sender, ILogger logger)
        {
            _partyManager = partyManager;
            _statusManager = statusManager;
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);

            var digimonHandle = packet.ReadInt();
            var evoStage = packet.ReadByte();

            var tamerId = client.TamerId;
            var evoLock = _evoLocks.GetOrAdd(tamerId, _ => new SemaphoreSlim(1, 1));
            await evoLock.WaitAsync();
            try
            {
                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

                if (client.Partner == null)
                {
                    _logger.Information($"[Evolution] FAIL | Sem parceiro ativo");
                    client.Send(new DigimonEvolutionFailPacket());
                    return;
                }
                client.blockAchievement = false;

                var evoLine = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == client.Partner.BaseType)?
                    .Lines.FirstOrDefault(x => x.Type == client.Partner.CurrentType)?.Stages;

                var evoInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == client.Partner.BaseType)?
                    .Lines.FirstOrDefault(x => x.Type == client.Partner.CurrentType);

                if (evoLine == null || !evoLine.Any() || evoStage < 0 || evoStage >= evoLine.Count)
                {
                    client.Send(new DigimonEvolutionFailPacket());
                    return;
                }

                // === Como no original: target SEMPRE vem de evoLine[evoStage] (inclui stage 8)
                var requestedType = evoLine[evoStage].Type;

                // evita spam/crash se já estiver no tipo solicitado
                if (requestedType == client.Partner.CurrentType)
                {
                    client.Send(new DigimonEvolutionFailPacket());
                    return;
                }

                var targetInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == client.Partner.BaseType)?
                    .Lines.FirstOrDefault(x => x.Type == requestedType);

                if (targetInfo == null)
                {
                    client.Send(new DigimonEvolutionFailPacket());
                    return;
                }

                var starterPartners = new List<int>() { 31001, 31002, 31003, 31004 };

                // Stage 8 (Back) não exige desbloqueio
                if (evoStage != 8)
                {
                    if (!client.Partner.BaseType.IsBetween(starterPartners.ToArray()))
                    {
                        var targetEvo = client.Partner.Evolutions.FirstOrDefault(x => x.Type == requestedType);
                        if (targetEvo == null || (targetEvo?.Unlocked ?? 0) == 0)
                        {
                            client.Send(new DigimonEvolutionFailPacket());
                            return;
                        }
                    }
                    else
                    {
                        var targetEvo = client.Partner.Evolutions.FirstOrDefault(x => x.Type == requestedType);
                        if (targetInfo.SlotLevel > 4 && (targetEvo == null || (targetEvo?.Unlocked ?? 0) == 0))
                        {
                            client.Send(new DigimonEvolutionFailPacket());
                            return;
                        }
                    }
                }

                // -- BUFFS --------------------------------
                var buffToRemove = client.Tamer.Partner.BuffList.TamerBaseSkill();
                if (buffToRemove != null)
                {
                    switch (mapConfig?.Type)
                    {
                        case MapTypeEnum.Dungeon:
                            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new RemoveBuffPacket(client.Partner.GeneralHandler, buffToRemove.BuffId).Serialize());
                            break;
                        case MapTypeEnum.Event:
                            _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new RemoveBuffPacket(client.Partner.GeneralHandler, buffToRemove.BuffId).Serialize());
                            break;
                        case MapTypeEnum.Pvp:
                            _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new RemoveBuffPacket(client.Partner.GeneralHandler, buffToRemove.BuffId).Serialize());
                            break;
                        default:
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new RemoveBuffPacket(client.Partner.GeneralHandler, buffToRemove.BuffId).Serialize());
                            break;
                    }
                }

                client.Tamer.RemovePartnerPassiveBuff();
                await _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));

                // ---------------------------------------
                DigimonEvolutionEffectEnum evoEffect;

                if (evoStage == 8)
                {
                    // Back (como no original): sem custos
                    evoEffect = DigimonEvolutionEffectEnum.Back;
                    client.Tamer.ActiveEvolution.SetDs(0);
                    client.Tamer.ActiveEvolution.SetXg(0);
                }
                else
                {
                    var evolutionType = _assets.DigimonBaseInfo.First(x => x.Type == requestedType).EvolutionType;

                    switch ((EvolutionRankEnum)evolutionType)
                    {
                        case EvolutionRankEnum.Rookie:
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            client.Tamer.ActiveEvolution.SetDs(0);
                            client.Tamer.ActiveEvolution.SetXg(0);
                            break;

                        case EvolutionRankEnum.Champion:
                            if (client.Partner.Level < targetInfo.UnlockLevel || !client.Tamer.ConsumeDs(20))
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            client.Tamer.ActiveEvolution.SetDs(8);
                            client.Tamer.ActiveEvolution.SetXg(0);
                            break;

                        case EvolutionRankEnum.Ultimate:
                            if (client.Partner.Level < targetInfo.UnlockLevel || !client.Tamer.ConsumeDs(50))
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            client.Tamer.ActiveEvolution.SetDs(10);
                            client.Tamer.ActiveEvolution.SetXg(0);
                            break;

                        case EvolutionRankEnum.Mega:
                            if (client.Partner.Level < targetInfo.UnlockLevel || !client.Tamer.ConsumeDs(152))
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            client.Tamer.ActiveEvolution.SetDs(12);
                            client.Tamer.ActiveEvolution.SetXg(0);
                            break;

                        case EvolutionRankEnum.BurstMode:
                            evoEffect = DigimonEvolutionEffectEnum.BurstMode;
                            if (targetInfo.RequiredItem > 0)
                            {
                                var itemToConsume = client.Tamer.Inventory.FindItemById(41002)
                                                     ?? client.Tamer.Inventory.FindItemById(9400);
                                if (itemToConsume == null)
                                {
                                    client.Send(new DigimonEvolutionFailPacket());
                                    return;
                                }
                                if (itemToConsume.Amount < targetInfo.RequiredAmount)
                                {
                                    client.Send(new DigimonEvolutionFailPacket());
                                    return;
                                }
                                if (client.Partner.Level < targetInfo.UnlockLevel && !client.Tamer.ConsumeDs(148))
                                {
                                    client.Send(new DigimonEvolutionFailPacket());
                                    return;
                                }
                                client.Tamer.Inventory.RemoveOrReduceItem(itemToConsume, targetInfo.RequiredAmount);
                            }
                            else
                            {
                                if (client.Partner.Level < targetInfo.UnlockLevel && !client.Tamer.ConsumeDs(148))
                                {
                                    client.Send(new DigimonEvolutionFailPacket());
                                    return;
                                }
                            }
                            client.Tamer.ActiveEvolution.SetDs(40);
                            client.Tamer.ActiveEvolution.SetXg(0);
                            break;

                        case EvolutionRankEnum.Jogress:
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            if (targetInfo.RequiredItem > 0)
                            {
                                var itemToConsume = client.Tamer.Inventory.FindItemBySection(targetInfo.RequiredItem)
                                                     ?? client.Tamer.Inventory.FindItemById(targetInfo.RequiredItem);
                                if (itemToConsume == null)
                                {
                                    client.Send(new DigimonEvolutionFailPacket());
                                    return;
                                }
                                if (!client.Tamer.Inventory.RemoveOrReduceItem(itemToConsume, targetInfo.RequiredAmount))
                                {
                                    client.Send(new DigimonEvolutionFailPacket());
                                    return;
                                }
                                if (client.Partner.Level < targetInfo.UnlockLevel && !client.Tamer.ConsumeDs(180))
                                {
                                    client.Send(new DigimonEvolutionFailPacket());
                                    return;
                                }
                            }
                            else
                            {
                                if (client.Partner.Level < targetInfo.UnlockLevel && !client.Tamer.ConsumeDs(180))
                                {
                                    client.Send(new DigimonEvolutionFailPacket());
                                    return;
                                }
                            }
                            client.Tamer.ActiveEvolution.SetDs(80);
                            client.Tamer.ActiveEvolution.SetXg(0);
                            break;

                        case EvolutionRankEnum.Capsule:
                            evoEffect = DigimonEvolutionEffectEnum.Unknown;
                            if (client.Partner.Level < targetInfo.UnlockLevel || !client.Tamer.ConsumeDs(75))
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            client.Tamer.ActiveEvolution.SetDs(3);
                            client.Tamer.ActiveEvolution.SetXg(0);
                            break;

                        case EvolutionRankEnum.Spirit:
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            if (client.Partner.Level < targetInfo.UnlockLevel)
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            client.Tamer.ActiveEvolution.SetDs(20);
                            client.Tamer.ActiveEvolution.SetXg(0);
                            break;

                        case EvolutionRankEnum.RookieX:
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            if (client.Partner.Level < targetInfo.UnlockLevel)
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            client.Tamer.ConsumeXg(68);
                            client.Tamer.ActiveEvolution.SetXg(2);
                            client.Tamer.ActiveEvolution.SetDs(0);
                            break;

                        case EvolutionRankEnum.ChampionX:
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            if (client.Partner.Level < targetInfo.UnlockLevel)
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            client.Tamer.ConsumeXg(92);
                            client.Tamer.ActiveEvolution.SetXg(4);
                            client.Tamer.ActiveEvolution.SetDs(0);
                            break;

                        case EvolutionRankEnum.UltimateX:
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            if (client.Partner.Level < targetInfo.UnlockLevel)
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            client.Tamer.ConsumeXg(130);
                            client.Tamer.ActiveEvolution.SetXg(6);
                            client.Tamer.ActiveEvolution.SetDs(0);
                            break;

                        case EvolutionRankEnum.MegaX:
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            if (client.Partner.Level < targetInfo.UnlockLevel)
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            client.Tamer.ConsumeXg(174);
                            client.Tamer.ActiveEvolution.SetXg(8);
                            client.Tamer.ActiveEvolution.SetDs(0);
                            break;

                        case EvolutionRankEnum.BurstModeX:
                            evoEffect = DigimonEvolutionEffectEnum.BurstMode;
                            if (client.Partner.Level < targetInfo.UnlockLevel)
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            client.Tamer.ConsumeXg(280);
                            client.Tamer.ActiveEvolution.SetXg(10);
                            client.Tamer.ActiveEvolution.SetDs(0);
                            break;

                        case EvolutionRankEnum.JogressX:
                            evoEffect = DigimonEvolutionEffectEnum.BurstMode;
                            if (client.Partner.Level < targetInfo.UnlockLevel)
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            client.Tamer.ConsumeXg(320);
                            client.Tamer.ActiveEvolution.SetXg(12);
                            client.Tamer.ActiveEvolution.SetDs(0);
                            break;

                        case EvolutionRankEnum.Extra:
                            evoEffect = DigimonEvolutionEffectEnum.Default;
                            if (client.Partner.Level < targetInfo.UnlockLevel)
                            {
                                client.Send(new DigimonEvolutionFailPacket());
                                return;
                            }
                            client.Tamer.ActiveEvolution.SetDs(20);
                            client.Tamer.ActiveEvolution.SetXg(0);
                            break;

                        default:
                            client.Send(new DigimonEvolutionFailPacket());
                            return;
                    }
                }

                // Atualiza o tipo depois de aplicar custos/efeitos (igual ao original)
                client.Partner.UpdateCurrentType(requestedType);

                // Riding → parar após mudança
                if (client.Tamer.Riding)
                {
                    client.Tamer.StopRideMode();
                    switch (mapConfig?.Type)
                    {
                        case MapTypeEnum.Dungeon:
                            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new RideModeStopPacket(client.Tamer.GeneralHandler, client.Partner.GeneralHandler).Serialize());
                            break;
                        case MapTypeEnum.Event:
                            _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new RideModeStopPacket(client.Tamer.GeneralHandler, client.Partner.GeneralHandler).Serialize());
                            break;
                        case MapTypeEnum.Pvp:
                            _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new RideModeStopPacket(client.Tamer.GeneralHandler, client.Partner.GeneralHandler).Serialize());
                            break;
                        default:
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new RideModeStopPacket(client.Tamer.GeneralHandler, client.Partner.GeneralHandler).Serialize());
                            break;
                    }
                }

                // Broadcast sucesso (usa o evoEffect definido)
                switch (mapConfig?.Type)
                {
                    case MapTypeEnum.Dungeon:
                        _dungeonServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new DigimonEvolutionSucessPacket(client.Tamer.GeneralHandler, client.Partner.GeneralHandler,
                                client.Partner.CurrentType, evoStage == 8 ? DigimonEvolutionEffectEnum.Back : evoEffect).Serialize());
                        break;
                    case MapTypeEnum.Event:
                        _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new DigimonEvolutionSucessPacket(client.Tamer.GeneralHandler, client.Partner.GeneralHandler,
                                client.Partner.CurrentType, evoStage == 8 ? DigimonEvolutionEffectEnum.Back : evoEffect).Serialize());
                        break;
                    case MapTypeEnum.Pvp:
                        _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new DigimonEvolutionSucessPacket(client.Tamer.GeneralHandler, client.Partner.GeneralHandler,
                                client.Partner.CurrentType, evoStage == 8 ? DigimonEvolutionEffectEnum.Back : evoEffect).Serialize());
                        break;
                    default:
                        _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                            new DigimonEvolutionSucessPacket(client.Tamer.GeneralHandler, client.Partner.GeneralHandler,
                                client.Partner.CurrentType, evoStage == 8 ? DigimonEvolutionEffectEnum.Back : evoEffect).Serialize());
                        break;
                }

                await UpdateSkillCooldown(client);

                var currentHp = client.Partner.CurrentHp;
                var currentMaxHp = client.Partner.HP;
                var currentDs = client.Partner.CurrentDs;
                var currentMaxDs = client.Partner.DS;

                client.Tamer.Partner.SetBaseInfo(_statusManager.GetDigimonBaseInfo(client.Tamer.Partner.CurrentType));
                client.Tamer.Partner.SetBaseStatus(_statusManager.GetDigimonBaseStatus(client.Tamer.Partner.CurrentType,
                    client.Tamer.Partner.Level, client.Tamer.Partner.Size));

                client.Partner.SetSealStatus(_assets.SealInfo);
                client.Tamer.SetPartnerPassiveBuff();

                if (evoStage != 8)
                {
                    client.Partner.FullHeal();
                }
                else
                {
                    client.Partner.AdjustHpAndDs(currentHp, currentMaxHp, currentDs, currentMaxDs);
                }

                var currentTitleBuff =
                    _assets.AchievementAssets.FirstOrDefault(x => x.QuestId == client.Tamer.CurrentTitle && x.BuffId > 0);

                if (currentTitleBuff != null)
                {
                    foreach (var buff in client.Tamer.Partner.BuffList.ActiveBuffs.Where(x => x.BuffId != currentTitleBuff.BuffId))
                        buff.SetBuffInfo(_assets.BuffInfo.FirstOrDefault(x =>
                            x.SkillCode == buff.SkillId && buff.BuffInfo == null ||
                            x.DigimonSkillCode == buff.SkillId && buff.BuffInfo == null));

                    if (client.Tamer.Partner.BuffList.TamerBaseSkill() != null)
                    {
                        var buffToApply = client.Tamer.Partner.BuffList.Buffs.Where(x => x.Duration == 0 && x.BuffId != currentTitleBuff.BuffId).ToList();
                        buffToApply.ForEach(digimonBuffModel =>
                        {
                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.Tamer.Id,
                                        new AddBuffPacket(client.Tamer.Partner.GeneralHandler, digimonBuffModel.BuffId,
                                            digimonBuffModel.SkillId, (short)digimonBuffModel.TypeN, 0).Serialize());
                                    break;
                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(client.Tamer.Id,
                                        new AddBuffPacket(client.Tamer.Partner.GeneralHandler, digimonBuffModel.BuffId,
                                            digimonBuffModel.SkillId, (short)digimonBuffModel.TypeN, 0).Serialize());
                                    break;
                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(client.Tamer.Id,
                                        new AddBuffPacket(client.Tamer.Partner.GeneralHandler, digimonBuffModel.BuffId,
                                            digimonBuffModel.SkillId, (short)digimonBuffModel.TypeN, 0).Serialize());
                                    break;
                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(client.Tamer.Id,
                                        new AddBuffPacket(client.Tamer.Partner.GeneralHandler, digimonBuffModel.BuffId,
                                            digimonBuffModel.SkillId, (short)digimonBuffModel.TypeN, 0).Serialize());
                                    break;
                            }
                        });
                    }
                }
                else
                {
                    foreach (var buff in client.Tamer.Partner.BuffList.ActiveBuffs)
                        buff.SetBuffInfo(_assets.BuffInfo.FirstOrDefault(x =>
                            x.SkillCode == buff.SkillId && buff.BuffInfo == null ||
                            x.DigimonSkillCode == buff.SkillId && buff.BuffInfo == null));

                    if (client.Tamer.Partner.BuffList.TamerBaseSkill() != null)
                    {
                        var buffToApply = client.Tamer.Partner.BuffList.Buffs.Where(x => x.Duration == 0).ToList();
                        buffToApply.ForEach(digimonBuffModel =>
                        {
                            switch (mapConfig?.Type)
                            {
                                case MapTypeEnum.Dungeon:
                                    _dungeonServer.BroadcastForTamerViewsAndSelf(client.Tamer.Id,
                                        new AddBuffPacket(client.Tamer.Partner.GeneralHandler, digimonBuffModel.BuffId,
                                            digimonBuffModel.SkillId, (short)digimonBuffModel.TypeN, 0).Serialize());
                                    break;
                                case MapTypeEnum.Event:
                                    _eventServer.BroadcastForTamerViewsAndSelf(client.Tamer.Id,
                                        new AddBuffPacket(client.Tamer.Partner.GeneralHandler, digimonBuffModel.BuffId,
                                            digimonBuffModel.SkillId, (short)digimonBuffModel.TypeN, 0).Serialize());
                                    break;
                                case MapTypeEnum.Pvp:
                                    _pvpServer.BroadcastForTamerViewsAndSelf(client.Tamer.Id,
                                        new AddBuffPacket(client.Tamer.Partner.GeneralHandler, digimonBuffModel.BuffId,
                                            digimonBuffModel.SkillId, (short)digimonBuffModel.TypeN, 0).Serialize());
                                    break;
                                default:
                                    _mapServer.BroadcastForTamerViewsAndSelf(client.Tamer.Id,
                                        new AddBuffPacket(client.Tamer.Partner.GeneralHandler, digimonBuffModel.BuffId,
                                            digimonBuffModel.SkillId, (short)digimonBuffModel.TypeN, 0).Serialize());
                                    break;
                            }
                        });
                    }
                }

                client.Send(new UpdateStatusPacket(client.Tamer));
                client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));

                // PARTY -------------------------------------------
                var party = _partyManager.FindParty(client.TamerId);
                if (party != null)
                {
                    party.UpdateMember(party[client.TamerId], client.Tamer);

                    foreach (var target in party.Members.Values)
                    {
                        var targetClient = _mapServer.FindClientByTamerId(target.Id)
                                           ?? _dungeonServer.FindClientByTamerId(target.Id)
                                           ?? _eventServer.FindClientByTamerId(target.Id)
                                           ?? _pvpServer.FindClientByTamerId(target.Id);

                        if (targetClient == null) continue;

                        if (target.Id != client.Tamer.Id)
                            targetClient.Send(new PartyMemberInfoPacket(party[client.TamerId]));
                    }

                }

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                await _sender.Send(new UpdatePartnerCurrentTypeCommand(client.Partner));
                await _sender.Send(new UpdateCharacterActiveEvolutionCommand(client.Tamer.ActiveEvolution));
                await _sender.Send(new UpdateCharacterBasicInfoCommand(client.Tamer));
                await _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));

            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[Evolution] Erro durante Process()");
                client.Send(new DigimonEvolutionFailPacket());
            }
            finally
            {
                evoLock.Release();
            }
        }

        private async Task UpdateSkillCooldown(GameClient client)
        {
            if (client.Tamer.Partner.HasActiveSkills())
            {
                foreach (var evolution in client.Tamer.Partner.Evolutions)
                {
                    foreach (var skill in evolution.Skills)
                    {
                        if (skill.Duration > 0 && skill.Expired)
                        {
                            skill.ResetCooldown();
                        }
                    }

                    await _sender.Send(new UpdateEvolutionCommand(evolution));
                }

                List<int> SkillIds = new List<int>(5);
                var packetEvolution =
                    client.Tamer.Partner.Evolutions.FirstOrDefault(x => x.Type == client.Tamer.Partner.CurrentType);

                if (packetEvolution != null)
                {
                    var slot = -1;
                    foreach (var item in packetEvolution.Skills)
                    {
                        slot++;
                        var skillInfo = _assets.DigimonSkillInfo.FirstOrDefault(x =>
                            x.Type == client.Partner.CurrentType && x.Slot == slot);
                        if (skillInfo != null)
                            SkillIds.Add(skillInfo.SkillId);
                    }

                    client?.Send(new SkillUpdateCooldownPacket(client.Tamer.Partner.GeneralHandler,
                        client.Tamer.Partner.CurrentType, packetEvolution, SkillIds));
                }
                else
                {
                    _logger.Information($"[Evolution] Sem packetEvolution para CurrentType={client.Tamer.Partner.CurrentType}");
                }
            }
            else
            {
                _logger.Information($"[Evolution] Sem skills ativas — nada a atualizar");
            }
        }
    }
}
