using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Models.Map;
using DigitalWorldOnline.Commons.Models.Summon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.Game;
using DigitalWorldOnline.Game.PacketProcessors;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.Commons.Packets.Chat;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using DigitalWorldOnline.Application.Separar.Commands.Update;

namespace DigitalWorldOnline.GameHost
{
    public sealed partial class DungeonsServer
    {
        public void TamerOperation(GameMap map)
        {
            if (!map.ConnectedTamers.Any())
            {
                map.SetNoTamers();
                return;
            }

            var sw = Stopwatch.StartNew();

            foreach (var tamer in map.ConnectedTamers)
            {
                var client = map.Clients
                                .FirstOrDefault(x => x.TamerId == tamer.Id);
                if (client?.IsConnected != true || client.Partner == null)
                    continue;
                DungeonDoors(map);
                ProcessDebuffs(client);
                ProcessVisibility(map, tamer, client);
                ProcessAttacks(tamer, client);
                CheckTimeReward(client);
                ProcessAttendanceReward(client);
                ProcessRegenAndEvolution(map, tamer, client);
                ProcessExpiredItems(client, tamer);
                ProcessBuffs(map, tamer, client);
                ProcessSyncResources(map, tamer, client);
                ProcessSaveResources(tamer);
                ProcessDailyQuestReset(client, tamer);
            }

            sw.Stop();
            if (sw.ElapsedMilliseconds >= 1000)
                Console.WriteLine(
                    $"TamersOperation ({map.ConnectedTamers.Count}): {sw.Elapsed.TotalMilliseconds}ms"
                );
        }

        private void DungeonDoors(GameMap map)
        {
            var mapObject = _assets.MapObjects.FirstOrDefault(mo => mo.MapId == map.MapId);

            if (mapObject == null)
                return;

            foreach (var mapSourceObject in mapObject.MapSourceObjects)
            {
                var factorId = mapSourceObject.ObjectId;

                foreach (var orderObjects in mapSourceObject.OrderObjects)
                {
                    foreach (var objects in orderObjects.Objects)
                    {
                        var mobType = objects.Factor;
                        var mob = map.Mobs.FirstOrDefault(x => x.Type == mobType);

                        if (mob != null)
                        {
                            byte doorState = mob.Alive ? (byte)0 : (byte)1;
                            map.BroadcastForMap(new DoorObjectOpenPacket(factorId, doorState).Serialize());
                        }
                    }
                }
            }
        }


        // 1) Debuffs e afins
        private void ProcessDebuffs(GameClient client)
        {
            CheckLocationDebuff(client);
            // MapBuff(client);
            // client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
        }

        // 2) Visibilidade de mobs, tamer e shop
        private void ProcessVisibility(GameMap map, CharacterModel tamer, GameClient client)
        {
            GetInViewMobs(map, tamer);
            GetInViewMobs(map, tamer, true);
            ShowOrHideTamer(map, tamer);
        }

        // 3) Ataques automáticos
        private void ProcessAttacks(CharacterModel tamer, GameClient client)
        {
            if (tamer.TargetIMobs.Count > 0)
                PartnerAutoAttackMob(client);

            if (tamer.TargetPartner != null)
            {
                tamer.StopBattle();
                tamer.Partner?.StopAutoAttack();
            }
        }

        // 4) Recompensa de presença mensal
        private void ProcessAttendanceReward(GameClient client)
        {
            if (client.Tamer.AttendanceReward.ReedemRewards)
                CheckMonthlyReward(client);
        }

        // 5) Regen e lógica de evolução interrompida
        private void ProcessRegenAndEvolution(GameMap map, CharacterModel tamer, GameClient client)
        {
            tamer.AutoRegen();
            tamer.ActiveEvolutionReduction();

            if (!tamer.BreakEvolution)
                return;

            tamer.ActiveEvolution.SetDs(0);
            tamer.ActiveEvolution.SetXg(0);

            if (tamer.Riding)
            {
                tamer.StopRideMode();
                BroadcastForTamerViewsAndSelf(
                    tamer.Id,
                    new UpdateMovementSpeedPacket(tamer).Serialize());
                BroadcastForTamerViewsAndSelf(
                    tamer.Id,
                    new RideModeStopPacket(tamer.GeneralHandler, tamer.Partner.GeneralHandler).Serialize());
            }

            var baseBuff = client.Tamer.Partner.BuffList.TamerBaseSkill();
            if (baseBuff != null)
            {
                BroadcastForTamerViewsAndSelf(
                    client.TamerId,
                    new RemoveBuffPacket(client.Partner.GeneralHandler, baseBuff.BuffId).Serialize());
            }

            client.Tamer.RemovePartnerPassiveBuff();

            map.BroadcastForTamerViewsAndSelf(
                tamer.Id,
                new DigimonEvolutionSucessPacket(
                    tamer.GeneralHandler,
                    tamer.Partner.GeneralHandler,
                    tamer.Partner.BaseType,
                    DigimonEvolutionEffectEnum.Back
                ).Serialize());

            // salvar stats antes de resetar
            var oldHp = client.Partner.CurrentHp;
            var oldMaxHp = client.Partner.HP;
            var oldDs = client.Partner.CurrentDs;
            var oldMaxDs = client.Partner.DS;

            tamer.Partner.UpdateCurrentType(tamer.Partner.BaseType);
            tamer.Partner.SetBaseInfo(
                _statusManager.GetDigimonBaseInfo(tamer.Partner.CurrentType));
            tamer.Partner.SetBaseStatus(
                _statusManager.GetDigimonBaseStatus(
                    tamer.Partner.CurrentType,
                    tamer.Partner.Level,
                    tamer.Partner.Size));

            client.Tamer.SetPartnerPassiveBuff();
            client.Partner.AdjustHpAndDs(oldHp, oldMaxHp, oldDs, oldMaxDs);

            foreach (var buff in client.Tamer.Partner.BuffList.ActiveBuffs)
            {
                buff.SetBuffInfo(
                    _assets.BuffInfo.FirstOrDefault(x =>
                        (x.SkillCode == buff.SkillId || x.DigimonSkillCode == buff.SkillId)
                        && buff.BuffInfo == null));
            }

            client.Send(new UpdateStatusPacket(tamer));

            if (client.Tamer.Partner.BuffList.TamerBaseSkill() != null)
            {
                var zeroDur = client.Tamer.Partner.BuffList.Buffs
                                 .Where(x => x.Duration == 0)
                                 .ToList();
                zeroDur.ForEach(digimonBuffModel =>
                {
                    BroadcastForTamerViewsAndSelf(
                        client.Tamer.Id,
                        new AddBuffPacket(
                            client.Tamer.Partner.GeneralHandler,
                            digimonBuffModel.BuffId,
                            digimonBuffModel.SkillId,
                            (short)digimonBuffModel.TypeN,
                            0
                        ).Serialize());
                });
            }

            var party = _partyManager.FindParty(client.TamerId);
            if (party != null)
            {
                party.UpdateMember(party[client.TamerId], client.Tamer);
                BroadcastForTargetTamers(
                    party.GetMembersIdList(),
                    new PartyMemberInfoPacket(party[client.TamerId]).Serialize());
            }

            _sender.Send(new UpdatePartnerCurrentTypeCommand(client.Partner));
            _sender.Send(new UpdateCharacterActiveEvolutionCommand(tamer.ActiveEvolution));
            _sender.Send(new UpdateDigimonBuffListCommand(client.Partner.BuffList));
        }

        // 6) Itens expirados em todas as abas
        private void ProcessExpiredItems(GameClient client, CharacterModel tamer)
        {
            if (!tamer.CheckExpiredItemsTime) return;

            tamer.SetLastExpiredItemsCheck();

            void Expire(IEnumerable<ItemModel> items, InventorySlotTypeEnum slotType, bool removeOnExpire)
            {
                foreach (var item in items)
                {
                    if (item.ItemInfo == null || !item.IsTemporary || !item.Expired)
                        continue;

                    if (item.ItemInfo.UseTimeType == 2 || item.ItemInfo.UseTimeType == 3)
                    {
                        item.SetFirstExpired(false);
                        client.Send(new ItemExpiredPacket(slotType, item.Slot, item.ItemId, ExpiredTypeEnum.Quit));
                    }
                    else if (removeOnExpire)
                    {
                        client.Send(new ItemExpiredPacket(slotType, item.Slot, item.ItemId, ExpiredTypeEnum.Remove));
                        // remove ou reduz quantidade
                        var container = slotType switch
                        {
                            InventorySlotTypeEnum.TabInven => tamer.Inventory,
                            InventorySlotTypeEnum.TabWarehouse => tamer.Warehouse,
                            InventorySlotTypeEnum.TabShareStash => tamer.AccountWarehouse!,
                            InventorySlotTypeEnum.TabEquip => tamer.Equipment,
                            InventorySlotTypeEnum.TabChipset => tamer.ChipSets,
                            _ => null
                        };
                        container?.RemoveOrReduceItem(item, item.Amount);

                    }
                    _sender.Send(new UpdateItemCommand(item));
                }
            }

            Expire(tamer.Inventory.EquippedItems, InventorySlotTypeEnum.TabInven, true);
            Expire(tamer.Warehouse.EquippedItems, InventorySlotTypeEnum.TabWarehouse, true);
            if (tamer.AccountWarehouse != null) Expire(tamer.AccountWarehouse.EquippedItems, InventorySlotTypeEnum.TabShareStash, true);
            Expire(tamer.Equipment.EquippedItems, InventorySlotTypeEnum.TabEquip, true);
            Expire(tamer.ChipSets.EquippedItems, InventorySlotTypeEnum.TabChipset, true);
        }

        // 7) Buffs do tamer e parceiro, mais skills em cash expirados
        private void ProcessBuffs(GameMap map, CharacterModel tamer, GameClient client)
        {
            if (tamer.CheckBuffsTime)
            {
                tamer.UpdateBuffsCheckTime();

                // remover buffs do tamer
                var expTamer = tamer.BuffList.Buffs.Where(x => x.Expired).ToList();
                foreach (var b in expTamer)
                {
                    tamer.BuffList.Remove(b.BuffId);
                    map.BroadcastForTamerViewsAndSelf(tamer.Id, new RemoveBuffPacket(tamer.GeneralHandler, b.BuffId).Serialize());
                }

                if (expTamer.Any())
                {
                    client.Send(new UpdateStatusPacket(tamer));
                    map.BroadcastForTargetTamers(tamer.Id, new UpdateCurrentHPRatePacket(tamer.GeneralHandler, tamer.HpRate).Serialize());
                    _sender.Send(new UpdateCharacterBuffListCommand(tamer.BuffList));
                }

                // remover buffs do parceiro
                var expPartner = tamer.Partner.BuffList.Buffs.Where(x => x.Expired).ToList();
                foreach (var b in expPartner)
                {
                    tamer.Partner.BuffList.Remove(b.BuffId);
                    map.BroadcastForTamerViewsAndSelf(tamer.Id, new RemoveBuffPacket(tamer.Partner.GeneralHandler, b.BuffId).Serialize());
                }
                if (expPartner.Any())
                {
                    client.Send(new UpdateStatusPacket(tamer));
                    map.BroadcastForTargetTamers(tamer.Id, new UpdateCurrentHPRatePacket(tamer.Partner.GeneralHandler, tamer.Partner.HpRate).Serialize());
                    _sender.Send(new UpdateDigimonBuffListCommand(tamer.Partner.BuffList));
                }

                // skills em Cash expiradas
                if (tamer.HaveActiveCashSkill)
                {
                    var expCash = tamer.ActiveSkill.Where(x => x.Expired && x.SkillId > 0 && x.Type == TamerSkillTypeEnum.Cash).ToList();
                    foreach (var b in expCash)
                    {
                        var active = tamer.ActiveSkill.First(x => x.Id == b.Id);
                        active.SetTamerSkill(0, 0, TamerSkillTypeEnum.Normal);
                        client.Send(new ActiveTamerCashSkillExpire(active.SkillId));
                        _sender.Send(new UpdateTamerSkillCooldownByIdCommand(active));
                    }
                }
            }
        }

        private void ProcessSyncResources(GameMap map, CharacterModel tamer, GameClient client)
        {
            if (!tamer.SyncResourcesTime)
                return;

            // Atualiza o timer interno
            tamer.UpdateSyncResourcesTime();

            // Novo estado consolidado
            var state = new SyncResourceState(
                Hp: (short)tamer.CurrentHp,
                Ds: (short)tamer.CurrentDs,
                Condition: (int)tamer.CurrentCondition,
                ShopName: tamer.ShopName,
                XGauge: (short)tamer.XGauge,
                XCrystals: (short)tamer.XCrystals
            );

            // Se é igual ao último, pula o envio
            // Se é igual ao último, pula o envio (exceto Xai)
            if (state == tamer.LastSyncState)
            {
                // Sempre envia o Xai para garantir atualização visual
                client.Send(new TamerXaiResourcesPacket(state.XGauge, state.XCrystals));

                // Atualiza o DS do tamer durante a evolução
                client.Send(new UpdateCurrentResourcesPacket(tamer.GeneralHandler, (short)tamer.CurrentHp, (short)tamer.CurrentDs, 0));

                return;
            }

            tamer.LastSyncState = state;

            // 1) Envia ao cliente apenas o que mudou
            client.Send(new UpdateCurrentResourcesPacket(tamer.GeneralHandler, state.Hp, state.Ds, 0));
            client.Send(new UpdateStatusPacket(tamer));
            client.Send(new TamerXaiResourcesPacket(state.XGauge, state.XCrystals));

            // 2) Broadcast de HP rate e condição para quem estiver vendo
            map.BroadcastForTargetTamers(
                tamer.Id,
                new UpdateCurrentHPRatePacket(
                    tamer.GeneralHandler, tamer.HpRate
                ).Serialize()
            );
            map.BroadcastForTargetTamers(
                tamer.Id,
                new UpdateCurrentHPRatePacket(
                    tamer.Partner.GeneralHandler, tamer.Partner.HpRate
                ).Serialize()
            );
            map.BroadcastForTamerViewsAndSelf(
                tamer.Id,
                new SyncConditionPacket(
                    tamer.GeneralHandler,
                    tamer.CurrentCondition,
                    tamer.ShopName
                ).Serialize()
            );

            // 3) Atualiza party de forma concisa
            var party = _partyManager.FindParty(tamer.Id);
            if (party != null)
            {
                if (party.Members.Count == 1)
                {
                    // kick slots 0–3
                    for (int slot = 0; slot < 4; slot++)
                        BroadcastForTargetTamers(
                            tamer.Id,
                            new PartyMemberKickPacket((byte)slot).Serialize()
                        );
                    _partyManager.RemoveParty(party.Id);
                }
                else
                {
                    party.UpdateMember(party[tamer.Id], tamer);
                    map.BroadcastForTargetTamers(
                        party.GetMembersIdList(),
                        new PartyMemberInfoPacket(party[tamer.Id]).Serialize()
                    );

                    var leaderEntry = party.GetMemberById(party.LeaderId);
                    if (leaderEntry != null)
                    {
                        party.LeaderSlot = leaderEntry.Value.Key;
                        BroadcastForTargetTamers(
                            party.GetMembersIdList(),
                            new PartyLeaderChangedPacket(
                                (int)leaderEntry.Value.Key
                            ).Serialize()
                        );
                    }

                    client.Send(new PartyMemberListPacket(party, tamer.Id));
                }
            }
        }

        // 9) SaveResourcesTime
        private void ProcessSaveResources(CharacterModel tamer)
        {
            if (!tamer.SaveResourcesTime)
                return;

            tamer.UpdateSaveResourcesTime();
            var sw = Stopwatch.StartNew();

            _sender.Send(new UpdateCharacterBasicInfoCommand(tamer));
            _sender.Send(new UpdateEvolutionCommand(tamer.Partner.CurrentEvolution));

            sw.Stop();
            if (sw.ElapsedMilliseconds >= 1500)
                Console.WriteLine($"Save resources elapsed time: {sw.ElapsedMilliseconds}ms");
        }

        // 10) Reset de daily quests
        private void ProcessDailyQuestReset(GameClient client, CharacterModel tamer)
        {
            if (!tamer.ResetDailyQuestsTime)
                return;

            tamer.UpdateDailyQuestsSyncTime();
            var resetTask = _sender.Send(new DailyQuestResetTimeQuery());

            if (DateTime.Now >= resetTask.Result)
                client.Send(new QuestDailyUpdatePacket());
        }
        private void GetInViewMobs(GameMap map, CharacterModel tamer)
        {
            List<long> mobsToAdd = new List<long>();
            List<long> mobsToRemove = new List<long>();

            // Criar uma cópia da lista de Mobs
            List<MobConfigModel> mobsCopy = new List<MobConfigModel>(map.Mobs);

            // Iterar sobre a cópia da lista
            mobsCopy.ForEach(mob =>
            {
                if (tamer.TempShowFullMap)
                {
                    if (!tamer.MobsInView.Contains(mob.Id))
                        mobsToAdd.Add(mob.Id);
                }
                else
                {
                    var distanceDifference = UtilitiesFunctions.CalculateDistance(
                        tamer.Location.X,
                        mob.CurrentLocation.X,
                        tamer.Location.Y,
                        mob.CurrentLocation.Y);

                    if (distanceDifference <= _startToSee && !tamer.MobsInView.Contains(mob.Id))
                        mobsToAdd.Add(mob.Id);

                    if (distanceDifference >= _stopSeeing && tamer.MobsInView.Contains(mob.Id))
                        mobsToRemove.Add(mob.Id);
                }
            });

            // Adicionar e remover os IDs de Mob na lista tamer.MobsInView após a iteração
            mobsToAdd.ForEach(id => tamer.MobsInView.Add(id));
            mobsToRemove.ForEach(id => tamer.MobsInView.Remove(id));
        }

        private void GetInViewMobs(GameMap map, CharacterModel tamer, bool Summon)
        {
            List<long> mobsToAdd = new List<long>();
            List<long> mobsToRemove = new List<long>();

            // Criar uma cópia da lista de Mobs
            List<SummonMobModel> mobsCopy = new List<SummonMobModel>(map.SummonMobs);

            // Iterar sobre a cópia da lista
            mobsCopy.ForEach(mob =>
            {
                if (tamer.TempShowFullMap)
                {
                    if (!tamer.MobsInView.Contains(mob.Id))
                        mobsToAdd.Add(mob.Id);
                }
                else
                {
                    var distanceDifference = UtilitiesFunctions.CalculateDistance(
                        tamer.Location.X,
                        mob.CurrentLocation.X,
                        tamer.Location.Y,
                        mob.CurrentLocation.Y);

                    if (distanceDifference <= _startToSee && !tamer.MobsInView.Contains(mob.Id))
                        mobsToAdd.Add(mob.Id);

                    if (distanceDifference >= _stopSeeing && tamer.MobsInView.Contains(mob.Id))
                        mobsToRemove.Add(mob.Id);
                }
            });

            // Adicionar e remover os IDs de Mob na lista tamer.MobsInView após a iteração
            mobsToAdd.ForEach(id => tamer.MobsInView.Add(id));
            mobsToRemove.ForEach(id => tamer.MobsInView.Remove(id));
        }

        /// <summary>
        /// Updates the current partners handler values;
        /// </summary>
        /// <param name="mapId">Current map id</param>
        /// <param name="digimons">Current digimons</param>
        public void SetDigimonHandlers(int mapId, List<DigimonModel> digimons)
        {
            Maps.FirstOrDefault(x => x.MapId == mapId)?.SetDigimonHandlers(digimons);
        }

        // -----------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Swaps the digimons current handler.
        /// </summary>
        /// <param name="mapId">Target map handler manager</param>
        /// <param name="oldPartnerId">Old partner identifier</param>
        /// <param name="newPartner">New partner</param>
        public void SwapDigimonHandlers(int mapId, DigimonModel oldPartner, DigimonModel newPartner)
        {
            Maps.FirstOrDefault(x => x.MapId == mapId)?.SwapDigimonHandlers(oldPartner, newPartner);
        }

        public void SwapDigimonHandlers(int mapId, int channel, DigimonModel oldPartner, DigimonModel newPartner)
        {
            Maps.FirstOrDefault(x => x.MapId == mapId && x.Channel == channel)?.SwapDigimonHandlers(oldPartner, newPartner);
        }

        // -----------------------------------------------------------------------------------------------------------------------

        private async void CheckLocationDebuff(GameClient client)
        {
            if (client.Tamer.DebuffTime)
            {
                client.Tamer.UpdateDebuffTime();

                // Verification for Dark Tower
                if (client.Tamer.Location.MapId == 1109)
                {
                    var mapDebuff = client.Partner.DebuffList.ActiveBuffs.Where(x => x.BuffId == 50101);
                    var evolutionType = _assets.DigimonBaseInfo.First(x => x.Type == client.Partner.CurrentType).EvolutionType;

                    if ((EvolutionRankEnum)evolutionType != EvolutionRankEnum.Rookie && (EvolutionRankEnum)evolutionType != EvolutionRankEnum.Capsule &&
                        (EvolutionRankEnum)evolutionType != EvolutionRankEnum.Spirit)
                    {
                        await Task.Delay(1000);
                        client.Tamer.IsSpecialMapActive = true;

                        client.Tamer.ActiveEvolution.SetDs(0);
                        client.Tamer.ActiveEvolution.SetXg(0);
                    }
                    else
                    {
                        client.Tamer.IsSpecialMapActive = false;
                    }

                    if (mapDebuff != null)
                    {

                        foreach (var teste in mapDebuff)
                        {
                        }

                    }

                }

                // Verifica Buff do PvpMap
                var buff1 = client.Tamer.BuffList.ActiveBuffs.FirstOrDefault(x => x.BuffId == 40345);

                if (buff1 != null)
                {
                    client.Tamer.BuffList.Buffs.Remove(buff1);

                    client.Send(new UpdateStatusPacket(client.Tamer));
                    client.Send(new RemoveBuffPacket(client.Tamer.GeneralHandler, buff1.BuffId).Serialize());
                }

                await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));
            }

        }

        private async void MapBuff(GameClient client)
        {
            var buff = _assets.BuffInfo.Where(x => x.BuffId == 40327 || x.BuffId == 40350).ToList();

            if (buff != null)
            {
                buff.ForEach(buffAsset =>
                {
                    if (!client.Tamer.BuffList.Buffs.Any(x => x.BuffId == buffAsset.BuffId))
                    {
                        var newCharacterBuff = CharacterBuffModel.Create(buffAsset.BuffId, buffAsset.SkillId, 2592000, 0);

                        newCharacterBuff.SetBuffInfo(buffAsset);

                        client.Tamer.BuffList.Buffs.Add(newCharacterBuff);

                        BroadcastForTamerViewsAndSelf(client.TamerId, new AddBuffPacket(client.Tamer.GeneralHandler, buffAsset, 0, 0).Serialize());
                    }
                });

                await _sender.Send(new UpdateCharacterBuffListCommand(client.Tamer.BuffList));
            }
        }

        // -----------------------------------------------------------------------------------------------------------------------

        private void ShowOrHideTamer(GameMap map, CharacterModel tamer)
        {
            foreach (var connectedTamer in map.ConnectedTamers.Where(x => x.Id != tamer.Id))
            {
                var distanceDifference = UtilitiesFunctions.CalculateDistance(
                    tamer.Location.X,
                    connectedTamer.Location.X,
                    tamer.Location.Y,
                    connectedTamer.Location.Y);

                if (distanceDifference <= _startToSee)
                    ShowTamer(map, tamer, connectedTamer.Id);
                else if (distanceDifference >= _stopSeeing)
                    HideTamer(map, tamer, connectedTamer.Id);
            }
        }

        private void ShowTamer(GameMap map, CharacterModel tamerToShow, long tamerToSeeId)
        {
            if (!map.ViewingTamer(tamerToShow.Id, tamerToSeeId))
            {
                foreach (var item in tamerToShow.Equipment.EquippedItems.Where(x => x.ItemInfo == null))
                    item?.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item?.ItemId));

                map.ShowTamer(tamerToShow.Id, tamerToSeeId);

                var targetClient = map.Clients.FirstOrDefault(x => x.TamerId == tamerToSeeId);
                if (targetClient != null)
                {
                    targetClient.Send(new LoadTamerPacket(tamerToShow));
                    targetClient.Send(new LoadBuffsPacket(tamerToShow));
                    if (tamerToShow.InBattle)
                    {
                        targetClient.Send(new SetCombatOnPacket(tamerToShow.GeneralHandler));
                        targetClient.Send(new SetCombatOnPacket(tamerToShow.Partner.GeneralHandler));
                    }
#if DEBUG
                    var serialized = SerializeShowTamer(tamerToShow);
                    //File.WriteAllText($"Shows\\Show{tamerToShow.Id}To{tamerToSeeId}_{DateTime.Now:dd_MM_yy_HH_mm_ss}.temp", serialized);
#endif
                }
            }
        }

        private void HideTamer(GameMap map, CharacterModel tamerToHide, long tamerToBlindId)
        {
            if (map.ViewingTamer(tamerToHide.Id, tamerToBlindId))
            {
                map.HideTamer(tamerToHide.Id, tamerToBlindId);

                var targetClient = map.Clients.FirstOrDefault(x => x.TamerId == tamerToBlindId);

                if (targetClient != null)
                {
                    targetClient.Send(new UnloadTamerPacket(tamerToHide));

#if DEBUG
                    var serialized = SerializeHideTamer(tamerToHide);
                    //File.WriteAllText($"Hides\\Hide{tamerToHide.Id}To{tamerToBlindId}_{DateTime.Now:dd_MM_yy_HH_mm_ss}.temp", serialized);
#endif
                }
            }
        }

        // -----------------------------------------------------------------------------------------------------------------------

        private static string SerializeHideTamer(CharacterModel tamer)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Tamer{tamer.Id}{tamer.Name}");
            sb.AppendLine($"TamerHandler {tamer.GeneralHandler.ToString()}");
            sb.AppendLine($"TamerLocation {tamer.Location.X.ToString()}");
            sb.AppendLine($"TamerLocation {tamer.Location.Y.ToString()}");

            sb.AppendLine($"Partner{tamer.Partner.Id}{tamer.Partner.Name}");
            sb.AppendLine($"PartnerHandler {tamer.Partner.GeneralHandler.ToString()}");
            sb.AppendLine($"PartnerLocation {tamer.Partner.Location.X.ToString()}");
            sb.AppendLine($"PartnerLocation {tamer.Partner.Location.Y.ToString()}");

            return sb.ToString();
        }

        private static string SerializeShowTamer(CharacterModel tamer)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Partner{tamer.Partner.Id}");
            sb.AppendLine($"PartnerName {tamer.Partner.Name}");
            sb.AppendLine($"PartnerLocation {tamer.Partner.Location.X.ToString()}");
            sb.AppendLine($"PartnerLocation {tamer.Partner.Location.Y.ToString()}");
            sb.AppendLine($"PartnerHandler {tamer.Partner.GeneralHandler.ToString()}");
            sb.AppendLine($"PartnerCurrentType {tamer.Partner.CurrentType.ToString()}");
            sb.AppendLine($"PartnerSize {tamer.Partner.Size.ToString()}");
            sb.AppendLine($"PartnerLevel {tamer.Partner.Level.ToString()}");
            sb.AppendLine($"PartnerModel {tamer.Partner.Model.ToString()}");
            sb.AppendLine($"PartnerMS {tamer.Partner.MS.ToString()}");
            sb.AppendLine($"PartnerAS {tamer.Partner.AS.ToString()}");
            sb.AppendLine($"PartnerHPRate {tamer.Partner.HpRate.ToString()}");
            sb.AppendLine($"PartnerCloneTotalLv {tamer.Partner.Digiclone.CloneLevel.ToString()}");
            sb.AppendLine($"PartnerCloneAtLv {tamer.Partner.Digiclone.ATLevel.ToString()}");
            sb.AppendLine($"PartnerCloneBlLv {tamer.Partner.Digiclone.BLLevel.ToString()}");
            sb.AppendLine($"PartnerCloneCtLv {tamer.Partner.Digiclone.CTLevel.ToString()}");
            sb.AppendLine($"PartnerCloneEvLv {tamer.Partner.Digiclone.EVLevel.ToString()}");
            sb.AppendLine($"PartnerCloneHpLv {tamer.Partner.Digiclone.HPLevel.ToString()}");

            sb.AppendLine($"Tamer{tamer.Id}");
            sb.AppendLine($"TamerName {tamer.Name.ToString()}");
            sb.AppendLine($"TamerLocation {tamer.Location.X.ToString()}");
            sb.AppendLine($"TamerLocation {tamer.Location.Y.ToString()}");
            sb.AppendLine($"TamerHandler {tamer.GeneralHandler.ToString()}");
            sb.AppendLine($"TamerModel {tamer.Model.ToString()}");
            sb.AppendLine($"TamerLevel {tamer.Level.ToString()}");
            sb.AppendLine($"TamerMS {tamer.MS.ToString()}");
            sb.AppendLine($"TamerHpRate {tamer.HpRate.ToString()}");
            sb.AppendLine($"TamerEquipment {tamer.Equipment.ToString()}");
            sb.AppendLine($"TamerDigivice {tamer.Digivice.ToString()}");
            sb.AppendLine($"TamerCurrentCondition {tamer.CurrentCondition.ToString()}");
            sb.AppendLine($"TamerSize {tamer.Size.ToString()}");
            sb.AppendLine($"TamerCurrentTitle {tamer.CurrentTitle.ToString()}");
            sb.AppendLine($"TamerSealLeaderId {tamer.SealList.SealLeaderId.ToString()}");

            return sb.ToString();
        }

        // -----------------------------------------------------------------------------------------------------------------------



        public void PartnerAutoAttackMob(GameClient client)
        {
            var tamer = client.Tamer;
            var partner = tamer.Partner;
            var targetMob = tamer.TargetIMob;

            if (!partner.AutoAttack || partner.IsAttacking || targetMob == null || !partner.Alive || !targetMob.Alive)
                return;

            #region Preparação para o ataque

            partner.SetEndAttacking(partner.AS);
            tamer.SetHidden(false);

            if (!tamer.InBattle)
            {
                BroadcastCombatOn(tamer.Id, partner.GeneralHandler);
                tamer.StartBattle(targetMob);
                partner.StartAutoAttack();
            }

            if (!targetMob.InBattle)
            {
                BroadcastCombatOn(tamer.Id, (ushort)targetMob.GeneralHandler);
                targetMob.StartBattle(tamer);
                partner.StartAutoAttack();
            }

            #endregion

            #region Cálculo de acerto

            if (tamer.CanMissHit())
            {
                _logger.Verbose($"Partner {partner.Id} missed hit on Mob {targetMob.Id} ({targetMob.Name}).");

                BroadcastForTamerViewsAndSelf(tamer.Id,
                    new MissHitPacket(partner.GeneralHandler, targetMob.GeneralHandler).Serialize());

                partner.UpdateLastHitTime();
                return;
            }

            #endregion

            #region Cálculo de dano

            var critBonusMultiplier = 0.00;
            var blocked = false;

            int finalDamage = tamer.GodMode
                ? targetMob.CurrentHP
                : AttackManager.CalculateDamage(client, out critBonusMultiplier, out blocked);

            finalDamage = Math.Clamp(finalDamage, 1, targetMob.CurrentHP);

            int newHp = targetMob.ReceiveDamage(finalDamage, tamer.Id);
            int hitType = blocked ? 2 : critBonusMultiplier > 0 ? 1 : 0;

            #endregion

            #region Feedback de ataque

            if (newHp > 0)
            {
                _logger.Debug($"Partner {partner.Id} hit Mob {targetMob.Id} - Damage: {finalDamage}, HP Left: {newHp}, Crit: {critBonusMultiplier > 0}, Blocked: {blocked}");

                BroadcastForTamerViewsAndSelf(
                    tamer.Id,
                    new HitPacket(partner.GeneralHandler, targetMob.GeneralHandler,
                        finalDamage, targetMob.HPValue, newHp, hitType).Serialize());
            }
            else
            {
                _logger.Debug($"Partner {partner.Id} killed Mob {targetMob.Id} with {finalDamage} damage. (HitType: {hitType})");

                BroadcastForTamerViewsAndSelf(
                    tamer.Id,
                    new KillOnHitPacket(partner.GeneralHandler, targetMob.GeneralHandler,
                        finalDamage, hitType).Serialize());

                targetMob.Die();

                if (!MobsAttacking(tamer.Location.MapId, tamer.Id))
                {
                    tamer.StopBattle();
                    BroadcastCombatOff(tamer.Id, partner.GeneralHandler);
                }
            }

            #endregion

            partner.UpdateLastHitTime();

            #region Verificação para encerrar ataque automático

            if (targetMob == null || targetMob.Dead)
                partner.StopAutoAttack();

            #endregion
        }

        private void BroadcastCombatOn(long tamerId, ushort handler)
        {
            BroadcastForTamerViewsAndSelf(tamerId, new SetCombatOnPacket(handler).Serialize());
        }

        private void BroadcastCombatOff(long tamerId, ushort handler)
        {
            BroadcastForTamerViewsAndSelf(tamerId, new SetCombatOffPacket(handler).Serialize());
        }
        // -----------------------------------------------------------------------------------------------------------------------

        private ReceiveExpResult ReceiveBonusTamerExp(CharacterModel tamer, long totalTamerExp)
        {
            var tamerResult = _expManager.ReceiveTamerExperience(totalTamerExp, tamer);

            if (tamerResult.LevelGain > 0)
            {
                BroadcastForTamerViewsAndSelf(tamer.Id,
                    new LevelUpPacket(tamer.GeneralHandler, tamer.Level).Serialize());

                tamer.SetLevelStatus(
                    _statusManager.GetTamerLevelStatus(
                        tamer.Model,
                        tamer.Level
                    )
                );

                tamer.FullHeal();
            }

            return tamerResult;
        }
        private ReceiveExpResult ReceiveTamerExp(CharacterModel tamer, long tamerExpToReceive)
        {
            var tamerResult = _expManager.ReceiveTamerExperience(tamerExpToReceive, tamer);

            if (tamerResult.LevelGain > 0)
            {
                BroadcastForTamerViewsAndSelf(tamer.Id,
                    new LevelUpPacket(tamer.GeneralHandler, tamer.Level).Serialize());

                tamer.SetLevelStatus(
                    _statusManager.GetTamerLevelStatus(
                        tamer.Model,
                        tamer.Level
                    )
                );

                tamer.FullHeal();
            }

            return tamerResult;
        }


        private ReceiveExpResult ReceiveBonusPartnerExp(DigimonModel partner, MobConfigModel targetMob, long totalPartnerExp)
        {
            var partnerResult = _expManager.ReceiveDigimonExperience(totalPartnerExp, partner);

            if (partnerResult.LevelGain > 0)
            {
                partner.SetBaseStatus(
                    _statusManager.GetDigimonBaseStatus(
                        partner.CurrentType,
                        partner.Level,
                        partner.Size
                    )
                );

                BroadcastForTamerViewsAndSelf(partner.Character.Id,
                    new LevelUpPacket(partner.GeneralHandler, partner.Level).Serialize());

                partner.FullHeal();
            }
            return partnerResult;
        }

        private ReceiveExpResult ReceivePartnerExp(GameClient client, DigimonModel partner, MobConfigModel targetMob, long partnerExpToReceive)
        {
            var attributeExp = partner.GetAttributeExperience();
            var elementExp = partner.GetElementExperience();

            var partnerResult = _expManager.ReceiveDigimonExperience(partnerExpToReceive, partner);

            if (attributeExp < 10000) _expManager.ReceiveAttributeExperience(client, partner, targetMob.Attribute, targetMob.ExpReward);
            if (elementExp < 10000) _expManager.ReceiveElementExperience(client, partner, targetMob.Element, targetMob.ExpReward);


            partner.ReceiveSkillExp(targetMob.ExpReward.SkillExperience);

            if (partnerResult.LevelGain > 0)
            {
                partner.SetBaseStatus(
                    _statusManager.GetDigimonBaseStatus(
                        partner.CurrentType,
                        partner.Level,
                        partner.Size
                    )
                );

                BroadcastForTamerViewsAndSelf(partner.Character.Id,
                    new LevelUpPacket(partner.GeneralHandler, partner.Level).Serialize());

                partner.FullHeal();
            }

            return partnerResult;
        }

        private ReceiveExpResult ReceivePartnerExp(GameClient client, DigimonModel partner, SummonMobModel targetMob, long partnerExpToReceive)
        {
            var attributeExp = partner.GetAttributeExperience();
            var elementExp = partner.GetElementExperience();
            var partnerResult = _expManager.ReceiveDigimonExperience(partnerExpToReceive, partner);

            if (attributeExp < 10000) _expManager.ReceiveAttributeExperience(client, partner, targetMob.Attribute, targetMob.ExpReward);
            if (elementExp < 10000) _expManager.ReceiveElementExperience(client, partner, targetMob.Element, targetMob.ExpReward);

            partner.ReceiveSkillExp(targetMob.ExpReward.SkillExperience);

            if (partnerResult.LevelGain > 0)
            {
                partner.SetBaseStatus(
                    _statusManager.GetDigimonBaseStatus(
                        partner.CurrentType,
                        partner.Level,
                        partner.Size
                    )
                );

                BroadcastForTamerViewsAndSelf(partner.Character.Id,
                    new LevelUpPacket(partner.GeneralHandler, partner.Level).Serialize());

                partner.FullHeal();
            }

            return partnerResult;
        }
        private ReceiveExpResult ReceiveBonusPartnerExp(DigimonModel partner, SummonMobModel targetMob, long totalPartnerExp)
        {
            var partnerResult = _expManager.ReceiveDigimonExperience(totalPartnerExp, partner);

            if (partnerResult.LevelGain > 0)
            {
                partner.SetBaseStatus(
                    _statusManager.GetDigimonBaseStatus(
                        partner.CurrentType,
                        partner.Level,
                        partner.Size
                    )
                );

                BroadcastForTamerViewsAndSelf(partner.Character.Id,
                    new LevelUpPacket(partner.GeneralHandler, partner.Level).Serialize());

                partner.FullHeal();
            }
            return partnerResult;
        }

        // -----------------------------------------------------------------------------------------------------------------------

        private async void CheckMonthlyReward(GameClient client)
        {
            int dayofmonth = DateTime.Now.Day;
            int currentMonth = DateTime.Now.Month;
            var attendanceReward = client.Tamer.AttendanceReward;

            if (attendanceReward.LastRewardDate.Month != currentMonth)
            {
                attendanceReward.TotalDays = 0;
                attendanceReward.SetLastRewardDate();
                await _sender.Send(new UpdateTamerAttendanceRewardCommand(attendanceReward));
            }

            if (attendanceReward.LastRewardDate.Day == dayofmonth)
            {
                return;
            }

            client.Tamer.AttendanceReward.IncreaseTotalDays();
            client.Tamer.AttendanceReward.SetLastRewardDate();
            ReedemReward(client);

            client.Send(new TamerAttendancePacket(attendanceReward.TotalDays));
            await _sender.Send(new UpdateTamerAttendanceRewardCommand(attendanceReward));
        }


        private async void CheckTimeReward(GameClient client)
        {
            var tr = client.Tamer.TimeReward;

            if (tr.ReedemRewards)
            {
                _logger.Debug($"Reward Index: {tr.RewardIndex}");
                tr.SetStartTime();
                await _sender.Send(new UpdateTamerAttendanceTimeRewardCommand(tr));
            }

            if (tr.RewardIndex > TimeRewardIndexEnum.Fourth)
            {
                tr.RewardIndex = TimeRewardIndexEnum.Ended;
                await _sender.Send(new UpdateTamerAttendanceTimeRewardCommand(tr));
                return;
            }

            if (tr.CurrentTime == 0)
                tr.CurrentTime = tr.AtualTime;

            if (DateTime.Now < tr.LastTimeRewardUpdate)
            {
                tr.SetLastTimeRewardDate();
                return;
            }

            tr.CurrentTime++;
            tr.UpdateCounter++;
            tr.SetAtualTime();

            if (tr.TimeCompleted())
            {
                ReedemTimeReward(client);
                tr.RewardIndex++;
                tr.CurrentTime = 0;
                tr.SetAtualTime();
                await _sender.Send(new UpdateTamerAttendanceTimeRewardCommand(tr));
            }
            else if (tr.UpdateCounter >= 60)
            {
                await _sender.Send(new UpdateTamerAttendanceTimeRewardCommand(tr));
                tr.UpdateCounter = 0;
            }

            if ((DateTime.Now - tr.LastPacketSent).TotalSeconds >= 60)
            {
                client.Send(new TimeRewardPacket(tr));
                tr.LastPacketSent = DateTime.Now;
            }

            tr.SetLastTimeRewardDate();
        }

        private void ReedemTimeReward(GameClient client)
        {
            var tr = client.Tamer.TimeReward;
            var drops = _assets.TimeRewardAssets
                            .Where(d => d.CurrentReward == (int)tr.RewardIndex);

            foreach (var drop in drops)
            {
                var reward = new ItemModel();
                reward.SetItemInfo(
                    _assets.ItemInfo.FirstOrDefault(i => i.ItemId == drop.ItemId)
                );
                reward.ItemId = drop.ItemId;
                reward.Amount = drop.ItemCount;

                if (reward.IsTemporary)
                    reward.SetRemainingTime((uint)reward.ItemInfo.UsageTimeMinutes);

                if (client.Tamer.Inventory.AddItem(reward))
                {
                    client.Send(new ReceiveItemPacket(reward, InventoryTypeEnum.Inventory));
                    _sender.Send(new UpdateItemCommand(reward));
                }
            }
        }

        private void ReedemReward(GameClient client)
        {
            var rewardInfo =
                _assets.MonthlyEvents.FirstOrDefault(x => x.CurrentDay == client.Tamer.AttendanceReward.TotalDays);

            if (rewardInfo != null)
            {
                var newItem = new ItemModel();
                newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == rewardInfo.ItemId));

                if (newItem.ItemInfo == null)
                {
                    _logger.Warning($"No item info found with ID {rewardInfo.ItemId} for tamer {client.TamerId}.");
                    client.Send(
                        new SystemMessagePacket($"No item info found with ID {rewardInfo.ItemId} by MonthEvent"));
                    return;
                }

                newItem.ItemId = rewardInfo.ItemId;
                newItem.Amount = rewardInfo.ItemCount;

                if (newItem.IsTemporary)
                    newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                newItem.EndDate = DateTime.Now.AddDays(7);

                var itemClone = (ItemModel)newItem.Clone();

                if (client.Tamer.GiftWarehouse.AddItemGiftStorage(newItem))
                {
                    _sender.Send(new UpdateItemsCommand(client.Tamer.GiftWarehouse));
                    client.Send(new SystemMessagePacket(
                        $"Added {newItem.ItemInfo.Name} x{newItem.Amount} to GiftStorage by MonthEvent"));
                }

                _sender.Send(new UpdateTamerAttendanceRewardCommand(client.Tamer.AttendanceReward));
            }
        }
        public void ForceUpdateTamers(GameMap map)
        {
            foreach (var tamer in map.ConnectedTamers)
            {
                var client = map.Clients.FirstOrDefault(x => x.TamerId == tamer.Id);
                if (client == null || !client.IsConnected)
                    continue;

                // 🔹 Reaplica visibilidade (mostra/oculta outros tamers)
                ShowOrHideTamer(map, tamer);
            }
        }

    }
}