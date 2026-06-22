using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Utils;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Serilog;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Packets.MapServer;
using Microsoft.Extensions.Configuration;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class QuestDeliverPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.QuestDeliver;

        private readonly StatusManager _statusManager;
        private readonly ExpManager _expManager;
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonsServer;
        private readonly EventServer _eventServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IConfiguration _configuration;

        public QuestDeliverPacketProcessor(
            StatusManager statusManager,
            ExpManager expManager,
            AssetsLoader assets,
            MapServer mapServer,
            DungeonsServer dungeonsServer,
            EventServer eventServer,
            PvpServer pvpServer,
            IConfiguration configuration,
            ILogger logger,
            ISender sender)
        {
            _statusManager = statusManager;
            _expManager = expManager;
            _mapServer = mapServer;
            _dungeonsServer = dungeonsServer;
            _eventServer = eventServer;
            _pvpServer = pvpServer;
            _assets = assets;
            _logger = logger;
            _configuration = configuration;
            _sender = sender;

        }

        private static readonly Dictionary<int, (int MapId, int WaypointIndex)> QuestMovementMap = new()
        {
            { 4501, (255, 1) }, // Exemplo: Quest 4501 move para o mapa 255 no waypoint 1
            { 4505, (254, 1) },
            { 4507, (250, 2) }, // Exemplo: Quest 4507 move para o mapa 250 no waypoint 2
            { 4580, (250, 4) },
            { 4598, (250, 4) },
            { 4610, (250, 4) },
            { 4623, (250, 4) },
            { 4635, (254, 8) },
            { 4662, (250, 4) },
            { 4663, (1800, 0) },
            { 4677, (1800, 1) },
            { 4688, (1800, 3) },
            { 4712, (1802, 0) },
            { 4715, (1800, 4) },
            { 4718, (1802, 1) },
            { 4733, (1803, 0) },
            { 4744, (1803, 1) },
            { 4746, (1805, 0) },
            { 4752, (1806, 1) },
            { 4763, (1807, 0) },
            { 4770, (1808, 0) },
            { 4776, (1801, 2) },
            { 4779, (254, 9) },
            { 4786, (265, 0) },
            { 4813, (263, 0) },
            { 4818, (263, 0) },
            { 4829, (263, 0) },
            { 4835, (269, 0) },

            // Adicione mais quests aqui
        };

        public async Task Process(GameClient client, byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);
            var questId = packet.ReadShort();
            var questInfo = _assets.Quest.FirstOrDefault(x => x.QuestId == questId);

            if (questInfo == null)
            {
                _logger.Error($"Unknown quest id {questId}.");
                client.Send(new SystemMessagePacket($"Unknown quest id {questId}."));
                client.Tamer.Progress.RemoveQuest(questId);
                return;
            }

            DeliverItems(client, questId, questInfo);
            ReturnSupplies(client, questId, questInfo);
            QuestRewards(client, questInfo);

            client.blockAchievement = false;

            if (QuestMovementMap.TryGetValue(questId, out var movementConfig))
            {
                await MovePlayerToMap(client, movementConfig.MapId, movementConfig.WaypointIndex);
            }

            // Log no Discord
            string discordMessage = $"**Quest Entregue**\n" +
                                    $"Tamer: {client.Tamer.Name}\n" +
                                    $"Quest ID: {questId}\n" +
                                    $"Tipo da Quest: {questInfo.QuestType}\n";

            // Itens entregues
            if (questInfo.QuestGoals.Any())
            {
                discordMessage += "**Itens Entregues:**\n";
                foreach (var goal in questInfo.QuestGoals)
                {
                    discordMessage += $"- {goal.GoalAmount}x (ID: {goal.GoalId})\n";
                }
            }

            // Recompensas recebidas
            if (questInfo.QuestRewards.Any())
            {
                discordMessage += "**Recompensas Recebidas:**\n";
                foreach (var reward in questInfo.QuestRewards)
                {
                    switch (reward.RewardType)
                    {
                        case QuestRewardTypeEnum.MoneyReward:
                            discordMessage += $"- Bits: {reward.RewardObjectList.Sum(x => x.Amount)}\n";
                            break;
                        case QuestRewardTypeEnum.ExperienceReward:
                            discordMessage += $"- Experiência: {reward.RewardObjectList.Sum(x => x.Amount)}\n";
                            break;
                        case QuestRewardTypeEnum.ItemReward:
                            discordMessage += $"- Itens:\n";
                            foreach (var item in reward.RewardObjectList)
                            {
                                discordMessage += $"  - {item.Amount}x (ID: {item.Reward})\n";
                            }
                            break;
                    }
                }
            }

            await _mapServer.CallDiscord(
                discordMessage,
                client,
                "00FF00", // Cor verde para indicar sucesso na entrega da quest
                "ENTREGA DE QUEST",
                "1374552506025119824" // ID do canal Discord
            );

            // Restante do código permanece inalterado
            var evolutionQuest = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == client.Partner.BaseType)?
                .Lines.FirstOrDefault(y => y.UnlockQuestId == questId && y.UnlockItemSection == 0);
            if (evolutionQuest != null)
            {
                var targetEvolution = client.Tamer.Partner.Evolutions[evolutionQuest.SlotLevel - 1];
                if (targetEvolution != null)
                {
                    targetEvolution.Unlock();
                    await _sender.Send(new UpdateEvolutionCommand(targetEvolution));
                    _logger.Verbose(
                        $"Character {client.TamerId} unlocked evolution {targetEvolution.Type} on quest {questId} completion.");
                    var evoInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == client.Partner.BaseType)?.Lines
                        .FirstOrDefault(x => x.Type == targetEvolution.Type);
                    var encyclopedia =
                        client.Tamer.Encyclopedia.First(x => x.DigimonEvolutionId == evoInfo.EvolutionId);
                    if (encyclopedia != null)
                    {
                        var encyclopediaEvolution =
                            encyclopedia.Evolutions.First(x => x.DigimonBaseType == targetEvolution.Type);
                        encyclopediaEvolution.Unlock();
                        await _sender.Send(new UpdateCharacterEncyclopediaEvolutionsCommand(encyclopediaEvolution));
                        int LockedEncyclopediaCount = encyclopedia.Evolutions.Count(x => x.IsUnlocked == false);
                        if (LockedEncyclopediaCount <= 0)
                        {
                            encyclopedia.SetRewardAllowed();
                            await _sender.Send(new UpdateCharacterEncyclopediaCommand(encyclopedia));
                        }
                    }
                }
            }

            var questToUpdate = client.Tamer.Progress.InProgressQuestData.FirstOrDefault(x => x.QuestId == questId);
            var id = client.Tamer.Progress.RemoveQuest(questId);
            if (questInfo.QuestType != QuestTypeEnum.RepeatableQuest && questInfo.QuestType != QuestTypeEnum.CombineQuest && questInfo.QuestType != QuestTypeEnum.RepeatableEventQuest)
            {
                UpdateProgressValue(client, questId);
            }

            client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
            await _sender.Send(new RemoveActiveQuestCommand(id));
            await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
            await _sender.Send(new UpdateItemListBitsCommand(client.Tamer.Inventory));
            await _sender.Send(new UpdateCharacterProgressCompleteCommand(client.Tamer.Progress));
        }

        private async Task MovePlayerToMap(GameClient client, int mapId, int waypointIndex)
        {
            var currentMapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

            // Remove o cliente do mapa atual com base no tipo de mapa
            switch (currentMapConfig.Type)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonsServer.RemoveClient(client);
                    break;
                case MapTypeEnum.Event:
                    _eventServer.RemoveClient(client);
                    break;
                case MapTypeEnum.Pvp:
                    _pvpServer.RemoveClient(client);
                    break;
                case MapTypeEnum.Default:
                    _mapServer.RemoveClient(client);
                    break;
            }

            // Obtém os waypoints do mapa de destino
            var waypoints = await _sender.Send(new MapRegionListAssetsByMapIdQuery(mapId));
            if (waypoints == null || !waypoints.Regions.Any())
            {
                _logger.Error($"Waypoints não encontrados para o mapa {mapId}.");
                client.Send(new SystemMessagePacket($"Waypoints não encontrados para o mapa {mapId}."));
                return;
            }

            // Obtém o waypoint específico
            var destination = waypoints.Regions.ElementAtOrDefault(waypointIndex);
            if (destination == null)
            {
                _logger.Error($"Waypoint inválido: {waypointIndex} para o mapa {mapId}.");
                client.Send(new SystemMessagePacket($"Waypoint inválido: {waypointIndex} para o mapa {mapId}."));
                return;
            }

            // Atualiza a localização do Tamer e do parceiro
            client.Tamer.NewLocation(mapId, destination.X, destination.Y);
            await _sender.Send(new UpdateCharacterLocationCommand(client.Tamer.Location));

            client.Tamer.Partner.NewLocation(mapId, destination.X, destination.Y);
            await _sender.Send(new UpdateDigimonLocationCommand(client.Tamer.Partner.Location));

            // Atualiza o estado do Tamer para "Loading"
            client.Tamer.UpdateState(CharacterStateEnum.Loading);
            await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

            client.SetGameQuit(false);

            // Envia o pacote de troca de mapa
            client.Send(new MapSwapPacket(
                _configuration["GameServer:PublicAddress"],
                _configuration["GameServer:Port"],
                mapId, destination.X, destination.Y).Serialize());
        }


        private int GetBitValue(int[] array, int x)
        {
            int arrIDX = x / 32;
            int bitPosition = x % 32;

            if (arrIDX >= array.Length)
            {
                _logger.Error($"Invalid array index.");
                throw new ArgumentOutOfRangeException("Invalid array index");
            }

            int value = array[arrIDX];
            return (value >> bitPosition) & 1;
        }

        private void SetBitValue(int[] array, int x, int bitValue)
        {
            int arrIDX = x / 32;
            int bitPosition = x % 32;

            if (arrIDX >= array.Length)
            {
                _logger.Error($"Invalid array index on set bit value.");
                throw new ArgumentOutOfRangeException("Invalid array index on set bit value.");
            }

            if (bitValue != 0 && bitValue != 1)
            {
                _logger.Error($"Invalid bit value. Only 0 or 1 are allowed.");
                throw new ArgumentException("Invalid bit value. Only 0 or 1 are allowed.");
            }

            int value = array[arrIDX];
            int mask = 1 << bitPosition;

            if (bitValue == 1)
                array[arrIDX] = value | mask;
            else
                array[arrIDX] = value & ~mask;
        }

        private void UpdateQuestComplete(GameClient client, int qIDX)
        {
            int intValue = GetBitValue(client.Tamer.Progress.CompletedDataValue, qIDX - 1);

            if (intValue == 0)
                SetBitValue(client.Tamer.Progress.CompletedDataValue, qIDX - 1, 1);
        }

        private void UpdateProgressValue(GameClient client, short questId)
        {
            UpdateQuestComplete(client, questId);
        }

        private void DeliverItems(GameClient client, short questId, QuestAssetModel questInfo)
        {
            foreach (var questGoal in questInfo.QuestGoals.Where(x => x.GoalType == QuestGoalTypeEnum.LootItem))
            {
                var item = new ItemModel();
                item.SetItemId(questGoal.GoalId);
                item.SetAmount(questGoal.GoalAmount);
                item.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId));
                WhyRYouGae(client, questGoal.GoalId, questGoal.GoalAmount);

                if (item.ItemInfo == null)
                {
                    _logger.Error($"Item information not found for item {item.ItemId}.");
                    client.Send(new SystemMessagePacket($"Item information not found for item {item.ItemId}."));
                }
                else
                {
                    _logger.Verbose(
                        $"Character {client.TamerId} delivered quest {questId} goal item {questGoal.GoalId} x{questGoal.GoalAmount}.");
                    client.Tamer.Inventory.RemoveOrReduceItem(item, questGoal.GoalAmount);
                }
            }
        }
        private void WhyRYouGae(GameClient client, int goalId, int goalAmount)
        {
            int totalItemCount = 0;

            for (int itemSlot = 0; itemSlot < client.Tamer.Inventory.Size; itemSlot++)
            {
                var targetItem = client.Tamer.Inventory.FindItemBySlotCheck(itemSlot);
                if (targetItem == null || targetItem.ItemId == 0)
                {
                    continue;
                }

                if (targetItem.ItemId == goalId)
                {
                    totalItemCount += targetItem.Amount;
                }
            }

            if (totalItemCount < goalAmount)
            {
                var banProcessor = SingletonResolver.GetService<BanForCheating>();
                var banMessage = banProcessor.BanAccountWithMessage(client.AccountId, client.Tamer.Name,
                    AccountBlockEnum.Permanent, $" Hatch without materials {totalItemCount} with quest wanting {goalAmount}", client,
                    "Why are you trying to cheat? Happy ban with video providing cheating.");

                var chatPacket = new NoticeMessagePacket(banMessage).Serialize();
                client.SendToAll(chatPacket);
                return;
            }
        }

        private void ReturnSupplies(GameClient client, short questId, QuestAssetModel questInfo)
        {
            foreach (var questSupply in questInfo.QuestSupplies)
            {
                var item = new ItemModel();
                item.SetItemId(questSupply.ItemId);
                item.SetAmount(questSupply.Amount);
                item.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId));

                if (item.ItemInfo == null)
                {
                    _logger.Error($"Item information not found for item {item.ItemId}.");
                    client.Send(new SystemMessagePacket($"Item information not found for item {item.ItemId}."));
                }
                else
                {
                    _logger.Verbose(
                        $"Character {client.TamerId} delivered quest {questId} supply item {questSupply.ItemId} x{questSupply.Amount}.");
                    client.Tamer.Inventory.RemoveOrReduceItem(item, questSupply.Amount);
                }
            }
        }

        private void QuestRewards(GameClient client, QuestAssetModel questInfo)
        {
            var questRewards = questInfo.QuestRewards;
            foreach (var questReward in questRewards)
            {
                switch (questReward.RewardType)
                {
                    case QuestRewardTypeEnum.MoneyReward:
                        {
                            QuestMoneyReward(client, questReward);
                        }
                        break;

                    case QuestRewardTypeEnum.ExperienceReward:
                        {
                            QuestExpReward(client, questReward);
                        }
                        break;

                    case QuestRewardTypeEnum.ItemReward:
                        {
                            QuestItemReward(client, questReward);
                        }
                        break;
                }
            }
        }

        private void QuestItemReward(GameClient client, QuestRewardAssetModel questReward)
        {
            questReward.RewardObjectList.ForEach(rewardObject =>
            {
                var newItem = new ItemModel();
                newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == rewardObject.Reward));

                if (newItem.ItemInfo == null)
                {
                    _logger.Warning($"No item info found with ID {rewardObject.Reward} for tamer {client.TamerId}.");
                    client.Send(new SystemMessagePacket($"No item info found with ID {rewardObject.Reward}."));
                    return;
                }

                newItem.SetItemId(rewardObject.Reward);
                newItem.SetAmount(rewardObject.Amount);

                if (newItem.IsTemporary)
                    newItem.SetRemainingTime((uint)newItem.ItemInfo.UsageTimeMinutes);

                var itemClone = (ItemModel)newItem.Clone();
                if (!client.Tamer.Inventory.AddItem(itemClone))
                {
                    client.Send(new PickItemFailPacket(PickItemFailReasonEnum.InventoryFull));
                }
                else
                {
                    _logger.Verbose(
                        $"Character {client.TamerId} received quest {questReward.Quest.QuestId} item {rewardObject.Reward} x{rewardObject.Amount} reward.");
                }
            });
        }

        private void QuestExpReward(GameClient client, QuestRewardAssetModel questReward)
        {
            questReward.RewardObjectList.ForEach(async rewardObject =>
            {

                var tamerExpToReceive = rewardObject.Amount / 10;
                var tamerResult = ReceiveTamerExp(client.Tamer, tamerExpToReceive);

                var partnerExpToReceive = rewardObject.Amount / 5;
                var partnerResult = ReceivePartnerExp(client.Partner, partnerExpToReceive);


                client.Send(
                    new ReceiveExpPacket(
                        tamerExpToReceive,
                        0, //TODO: obter os bonus
                        client.Tamer.CurrentExperience,
                        client.Partner.GeneralHandler,
                        partnerExpToReceive,
                        0, //TODO: obter os bonus
                        client.Partner.CurrentExperience,
                        client.Partner.CurrentEvolution.SkillExperience
                    )
                );

                var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));

                if (tamerResult.LevelGain > 0 || partnerResult.LevelGain > 0)
                {
                    client.Send(new UpdateStatusPacket(client.Tamer));
                    switch (mapConfig?.Type)
                    {
                        case MapTypeEnum.Dungeon:
                            _dungeonsServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            break;

                        case MapTypeEnum.Event:
                            _eventServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            break;

                        case MapTypeEnum.Pvp:
                            _pvpServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            break;

                        default:
                            _mapServer.BroadcastForTamerViewsAndSelf(client.TamerId,
                                new UpdateMovementSpeedPacket(client.Tamer).Serialize());
                            break;
                    }
                }

                if (tamerResult.Success)
                {
                    await _sender.Send(
                        new UpdateCharacterExperienceCommand(
                            client.TamerId,
                            client.Tamer.CurrentExperience,
                            client.Tamer.Level
                        )
                    );
                }

                if (partnerResult.Success)
                {
                    await _sender.Send(
                        new UpdateDigimonExperienceCommand(
                            client.Partner
                        )
                    );
                }
            });
        }

        private void QuestMoneyReward(GameClient client, QuestRewardAssetModel questReward)
        {
            questReward.RewardObjectList.ForEach(rewardObject =>
            {
                _logger.Verbose(
                    $"Character {client.TamerId} received quest {questReward.Quest.QuestId} {rewardObject.Amount} bits reward.");
                client.Tamer.Inventory.AddBits(rewardObject.Amount);
            });
        }

        private ReceiveExpResult ReceiveTamerExp(CharacterModel tamer, long tamerExpToReceive)
        {
            var tamerResult = _expManager.ReceiveTamerExperience(tamerExpToReceive, tamer);

            if (tamerResult.LevelGain > 0)
            {
                _mapServer.BroadcastForTamerViewsAndSelf(tamer.Id,
                    new LevelUpPacket(tamer.GeneralHandler, tamer.Level).Serialize());
                _dungeonsServer.BroadcastForTamerViewsAndSelf(tamer.Id,
                    new LevelUpPacket(tamer.GeneralHandler, tamer.Level).Serialize());
                _eventServer.BroadcastForTamerViewsAndSelf(tamer.Id,
                    new LevelUpPacket(tamer.GeneralHandler, tamer.Level).Serialize());
                _pvpServer.BroadcastForTamerViewsAndSelf(tamer.Id,
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

        private ReceiveExpResult ReceivePartnerExp(DigimonModel partner, long partnerExpToReceive)
        {
            var partnerResult = _expManager.ReceiveDigimonExperience(partnerExpToReceive, partner);

            if (partnerResult.LevelGain > 0)
            {
                partner.SetBaseStatus(
                    _statusManager.GetDigimonBaseStatus(
                        partner.CurrentType,
                        partner.Level,
                        partner.Size
                    )
                );

                _mapServer.BroadcastForTamerViewsAndSelf(partner.Character.Id,
                    new LevelUpPacket(partner.GeneralHandler, partner.Level).Serialize());

                _dungeonsServer.BroadcastForTamerViewsAndSelf(partner.Character.Id,
                    new LevelUpPacket(partner.GeneralHandler, partner.Level).Serialize());

                _eventServer.BroadcastForTamerViewsAndSelf(partner.Character.Id,
                    new LevelUpPacket(partner.GeneralHandler, partner.Level).Serialize());

                _pvpServer.BroadcastForTamerViewsAndSelf(partner.Character.Id,
                    new LevelUpPacket(partner.GeneralHandler, partner.Level).Serialize());

                partner.FullHeal();
            }

            return partnerResult;
        }
    }
}