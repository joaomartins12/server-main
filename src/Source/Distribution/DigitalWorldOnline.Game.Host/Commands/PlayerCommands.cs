using AutoMapper;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.Chat;
using DigitalWorldOnline.Commons.Packets.GameServer.Combat;
using DigitalWorldOnline.Commons.Packets.Items;
using DigitalWorldOnline.Commons.Packets.MapServer;
using DigitalWorldOnline.Game.Managers;
using DigitalWorldOnline.GameHost;
using DigitalWorldOnline.GameHost.EventsServer;
using MediatR;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Text;
using System.Text.RegularExpressions;

namespace DigitalWorldOnline.Game
{
    public sealed class PlayerCommands : IDisposable
    {
        private const string GameServerAddress = "GameServer:Address";
        private const string GamerServerPublic = "GameServer:PublicAddress";
        private const string GameServerPort = "GameServer:Port";

        private readonly PartyManager _partyManager;
        private readonly StatusManager _statusManager;
        private readonly ExpManager _expManager;
        private readonly AssetsLoader _assets;
        private readonly MapServer _mapServer;
        private readonly DungeonsServer _dungeonServer;
        private readonly PvpServer _pvpServer;
        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IMapper _mapper;
        private readonly IConfiguration _configuration;

        private Dictionary<string, (Func<GameClient, string[], Task> Command, List<AccountAccessLevelEnum> AccessLevels)> commands;

        public PlayerCommands(PartyManager partyManager, StatusManager statusManager, ExpManager expManager, AssetsLoader assets,
            MapServer mapServer, DungeonsServer dungeonsServer, IMapper mapper, PvpServer pvpServer, ILogger logger, ISender sender, IConfiguration configuration)
        {
            _partyManager = partyManager;
            _expManager = expManager;
            _statusManager = statusManager;
            _assets = assets;
            _mapServer = mapServer;
            _dungeonServer = dungeonsServer;
            _pvpServer = pvpServer;
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
            _configuration = configuration;
            InitializeCommands();
        }

        // Comands and permissions
        private void InitializeCommands()
        {
            commands = new Dictionary<string, (Func<GameClient, string[], Task> Command, List<AccountAccessLevelEnum> AccessLevels)>
            {
                { "battlelog", (BattleLogCommand, null) },
                { "stats", (StatsCommand, null) },
                { "time", (TimeCommand, null) },
                { "deckload", (DeckLoadCommand, null) },
                { "pvp", (PvpCommand, new List<AccountAccessLevelEnum> { AccountAccessLevelEnum.Vip, AccountAccessLevelEnum.Vip2, AccountAccessLevelEnum.Vip3, AccountAccessLevelEnum.Vip4, AccountAccessLevelEnum.Vip5 }) },
                { "help", (HelpCommand, null) },
                { "critical", (CriticalCommand, null) },
                { "timeboss", (TimeBossCommand, null) },
                { "updateevo", (UpdateEvoCommand, null) },
                { "battle", (battleCommand, null) },
                { "itemfind", (ItemFindCommand, null) } // Novo comando adicionado
            };
        }

        private async Task battleCommand(GameClient client, string[] command)
        {
            var tamer = client.Tamer;

            // Força a remoção do estado de batalha
            tamer.InBattle = false;
            tamer.TargetMobs.Clear();
            tamer.StopBattle();

            // Remove mobs que possam estar marcando o Tamer como alvo (garantia extra)
            foreach (var mob in _mapServer.Maps.FirstOrDefault(map => map.MapId == tamer.Location.MapId)?.Mobs ?? Enumerable.Empty<MobConfigModel>())
            {
                mob.TargetTamers.RemoveAll(x => x.Id == tamer.Id);
            }

            // Envia o pacote para o próprio jogador
            client.Send(new SetCombatOffPacket(tamer.Partner.GeneralHandler).Serialize());

            // E também envia para os outros ao redor (se quiser)
            _mapServer.BroadcastForTamerViewsAndSelf(tamer.Id,
                new SetCombatOffPacket(tamer.Partner.GeneralHandler).Serialize());

            client.Send(new NoticeMessagePacket("Modo de combate forcado foi removido."));
        }


        private async Task TimeBossCommand(GameClient client, string[] command)
        {
            var currentMapId = client.Tamer.Location.MapId;
            var currentChannel = client.Tamer.Channel;

            var bossList = new List<string>();

            foreach (var map in _mapServer.Maps)
            {
                if (map.MapId != currentMapId || map.Channel != currentChannel)
                    continue;

                foreach (var mob in map.Mobs)
                {
                    if (mob?.Class != 8) continue; // 8 = Boss

                    if (mob.Alive)
                        continue;

                    if (!mob.ResurrectionTime.HasValue || !mob.DeathTime.HasValue)
                        continue;

                    var remaining = (mob.ResurrectionTime.Value - DateTime.UtcNow).TotalSeconds;

                    if (remaining <= 0)
                        continue;

                    var formattedRespawn = $"{(int)(remaining / 60):D2}:{(int)(remaining % 60):D2}";
                    var message = $"[{map.Name} CH{map.Channel}] {mob.Name} - Respawn em {formattedRespawn} (Morreu às {mob.DeathTime.Value:HH:mm:ss}, volta às {mob.ResurrectionTime.Value:HH:mm:ss})";

                    bossList.Add(message);
                }
            }

            if (!bossList.Any())
            {
                client.Send(new SystemMessagePacket("Nenhum boss aguardando respawn no seu mapa atual."));
                return;
            }

            client.Send(new SystemMessagePacket("Bosses aguardando respawn no seu mapa:"));
            foreach (var boss in bossList)
            {
                client.Send(new SystemMessagePacket(boss));
            }
        }

        // Plano em pseudocódigo:
        // 1. Antes de criar um novo Digimon (CreateDigimonCommand), remova ou limpe o campo Id de DigimonLocationDTO e qualquer entidade dependente que faça parte da chave composta.
        // 2. Certifique-se de que, ao atualizar ou criar, não está tentando modificar o campo Id de uma entidade já rastreada pelo EF Core.
        // 3. Se necessário, crie uma nova instância de DigimonLocationDTO ao invés de reutilizar uma existente com Id já definido.

        private async Task UpdateEvoCommand(GameClient client, string[] command)
        {
            // Impede o uso do comando se o jogador estiver em Dungeon
            var mapConfig = await _sender.Send(new GameMapConfigByMapIdQuery(client.Tamer.Location.MapId));
            if (mapConfig.Type == MapTypeEnum.Dungeon)
            {
                client.Send(new SystemMessagePacket("Este comando nao pode ser usado dentro de uma Dungeon."));
                return;
            }

            var currentDigimon = client.Partner;
            if (currentDigimon == null)
            {
                client.Send(new SystemMessagePacket("You don't have a Digimon selected."));
                return;
            }

            var digiId = currentDigimon.BaseType;
            var evoInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == digiId);
            if (evoInfo == null)
            {
                client.Send(new SystemMessagePacket($"No evolution info found for Digimon {digiId}."));
                return;
            }

            // Loga o level das skills de todas as evoluções do digimon
            //  foreach (var evo in currentDigimon.Evolutions)
            //   {
            //    var skillsInfo = evo.Skills.Select(skill => $"SkillId: {skill.Id}, Level: {skill.CurrentLevel}").ToList();
            //   var skillsLog = skillsInfo.Any()
            //               ? string.Join("\n    ", skillsInfo)
            //              : "    Nenhuma skill encontrada";
            //   var evoName = _assets.DigimonBaseInfo.FirstOrDefault(x => x.Type == evo.Type)?.Name ?? "Unknown";
            //   _logger.Information($"\n[updateevo] DigimonId: {currentDigimon.Id}\n  EvolutionType: {evo.Type}\n  EvolutionName: {evoName}\n  Skills:\n    {skillsLog}");
            //  }

            // Salva o status de desbloqueio de cada tipo de evolução
            var previousEvos = currentDigimon.Evolutions.ToDictionary(
                evo => evo.Type,
                evo => evo.Unlocked
            );

            // Salva as evoluções que tinham todas as skills em lv10, lv15, lv20, lv25
            var evosComSkillsLv10 = currentDigimon.Evolutions.Count(evo =>
                evo.Skills.Count > 0 && evo.Skills.All(s => s.CurrentLevel == 10)
            );
            var evosComSkillsLv15 = currentDigimon.Evolutions.Count(evo =>
                evo.Skills.Count > 0 && evo.Skills.All(s => s.CurrentLevel == 15)
            );
            var evosComSkillsLv20 = currentDigimon.Evolutions.Count(evo =>
                evo.Skills.Count > 0 && evo.Skills.All(s => s.CurrentLevel == 20)
            );
            var evosComSkillsLv25 = currentDigimon.Evolutions.Count(evo =>
                evo.Skills.Count > 0 && evo.Skills.All(s => s.CurrentLevel == 25)
            );

            // --- VERIFICAÇÃO DE ESPAÇO NO INVENTÁRIO ANTES DE ATUALIZAR ---
            int totalItensParaEntregar = evosComSkillsLv10 + evosComSkillsLv15 + evosComSkillsLv20 + evosComSkillsLv25;
            int slotsNecessarios = 0;
            var inventory = client.Tamer.Inventory;

            int[] itemIds = { 59063, 59064, 59065, 59066 };
            int[] quantidades = { evosComSkillsLv10, evosComSkillsLv15, evosComSkillsLv20, evosComSkillsLv25 };

            for (int idx = 0; idx < itemIds.Length; idx++)
            {
                int itemId = itemIds[idx];
                int quantidade = quantidades[idx];
                if (quantidade <= 0) continue;
                var itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId);
                if (itemInfo == null) continue;

                int quantidadeRestante = quantidade;
                foreach (var existing in inventory.Items.Where(x =>
                    x.ItemId == itemId &&
                    x.ItemInfo?.Overlap > 1 &&
                    x.Amount < x.ItemInfo.Overlap))
                {
                    int podeAdicionar = Math.Min(quantidadeRestante, existing.ItemInfo!.Overlap - existing.Amount);
                    quantidadeRestante -= podeAdicionar;
                    if (quantidadeRestante <= 0) break;
                }
                slotsNecessarios += quantidadeRestante;
            }

            int slotsVazios = inventory.Items.Count(x => x.ItemId == 0);
            if (slotsNecessarios > slotsVazios)
            {
                client.Send(new SystemMessagePacket("Seu inventario nao possui espaço suficiente para receber todos os itens especiais de skill. Libere espaço antes de usar este comando."));
                return;
            }
            // --- FIM DA VERIFICAÇÃO DE ESPAÇO ---

            // Atualiza linha evolutiva
            currentDigimon.Evolutions.Clear();
            currentDigimon.AddEvolutions(evoInfo);

            foreach (var evo in currentDigimon.Evolutions)
            {
                if (previousEvos.TryGetValue(evo.Type, out var previousUnlocked))
                {
                    if ((previousUnlocked & 1) != 0) evo.Unlock();
                    if ((previousUnlocked & 8) != 0) evo.UnlockRide();
                }
            }

            var digimonInfo = await _sender.Send(new CreateDigimonCommand(currentDigimon));
            if (digimonInfo != null)
            {
                currentDigimon.SetId(digimonInfo.Id);

                for (int i = 0; i < currentDigimon.Evolutions.Count; i++)
                {
                    var evolution = digimonInfo.Evolutions.ElementAtOrDefault(i);
                    if (evolution != null)
                    {
                        currentDigimon.Evolutions[i].SetId(evolution.Id);

                        for (int j = 0; j < currentDigimon.Evolutions[i].Skills.Count; j++)
                        {
                            var skill = evolution.Skills.ElementAtOrDefault(j);
                            if (skill != null)
                                currentDigimon.Evolutions[i].Skills[j].SetId(skill.Id);
                        }
                    }
                }
            }

            // Entrega itens especiais
            async Task EntregarItemEspecial(int itemId, int quantidade, string mensagem)
            {
                if (quantidade <= 0) return;
                var itemInfo = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId);
                if (itemInfo == null) return;

                int entregues = 0;

                foreach (var existing in inventory.Items.Where(x =>
                    x.ItemId == itemId &&
                    x.ItemInfo?.Overlap > 1 &&
                    x.Amount < x.ItemInfo.Overlap))
                {
                    int podeAdicionar = Math.Min(quantidade - entregues, existing.ItemInfo!.Overlap - existing.Amount);
                    if (podeAdicionar > 0)
                    {
                        existing.IncreaseAmount(podeAdicionar);
                        existing.SetSlot(existing.Slot);

                        var itemVisual = (ItemModel)existing.Clone();
                        itemVisual.Amount = podeAdicionar;
                        client.Send(new ReceiveItemPacket(itemVisual, InventoryTypeEnum.Inventory, existing.Slot));

                        await _sender.Send(new UpdateItemCommand(existing));
                        entregues += podeAdicionar;
                        if (entregues >= quantidade) break;
                    }
                }

                while (entregues < quantidade)
                {
                    int emptySlot = inventory.GetEmptySlot;
                    if (emptySlot == -1)
                    {
                        client.Send(new SystemMessagePacket("Seu inventario esta cheio. Nao foi possível entregar todos os itens especiais."));
                        break;
                    }

                    var existing = inventory.Items.FirstOrDefault(x =>
                        x.ItemId == itemId &&
                        x.ItemInfo?.Overlap > 1 &&
                        x.Amount < x.ItemInfo.Overlap);

                    if (existing != null)
                    {
                        int podeAdicionar = Math.Min(quantidade - entregues, existing.ItemInfo.Overlap - existing.Amount);
                        if (podeAdicionar > 0)
                        {
                            existing.IncreaseAmount(podeAdicionar);
                            existing.SetSlot(existing.Slot);
                            client.Send(new ReceiveItemPacket(existing, InventoryTypeEnum.Inventory, existing.Slot));
                            await _sender.Send(new UpdateItemCommand(existing));
                            entregues += podeAdicionar;
                            if (entregues >= quantidade) break;
                        }
                    }
                    else
                    {
                        var newItem = new ItemModel();
                        newItem.SetItemInfo(itemInfo);
                        newItem.ItemId = itemId;
                        newItem.Amount = 1;
                        newItem.SetSlot(emptySlot);
                        if (newItem.IsTemporary && itemInfo.UsageTimeMinutes > 0)
                            newItem.SetRemainingTime((uint)itemInfo.UsageTimeMinutes);
                        inventory.InsertItem(newItem);
                        client.Send(new ReceiveItemPacket(newItem, InventoryTypeEnum.Inventory, emptySlot));
                        await _sender.Send(new UpdateItemCommand(newItem));
                        entregues++;
                    }
                }

                if (entregues > 0)
                    client.Send(new SystemMessagePacket($"{mensagem} ({entregues}x)"));
            }

            await EntregarItemEspecial(59063, evosComSkillsLv10, "Parabens! Voce recebeu o item especial por ter todas as skills de uma evolucao no level 10!");
            await EntregarItemEspecial(59064, evosComSkillsLv15, "Parabens! Voce recebeu o item especial por ter todas as skills de uma evolucao no level 15!");
            await EntregarItemEspecial(59065, evosComSkillsLv20, "Parabens! Voce recebeu o item especial por ter todas as skills de uma evolucao no level 20!");
            await EntregarItemEspecial(59066, evosComSkillsLv25, "Parabens! Voce recebeu o item especial por ter todas as skills de uma evolucao no level 25!");

            // RELOAD automático após atualizar as evoluções
            client.Tamer.UpdateState(CharacterStateEnum.Loading);
            await _sender.Send(new UpdateCharacterStateCommand(client.TamerId, CharacterStateEnum.Loading));

            switch (mapConfig.Type)
            {
                case MapTypeEnum.Dungeon:
                    _dungeonServer.RemoveClient(client);
                    break;
                case MapTypeEnum.Pvp:
                    _pvpServer.RemoveClient(client);
                    break;
                default:
                    _mapServer.RemoveClient(client);
                    break;
            }

            client.SetGameQuit(false);
            client.Tamer.UpdateSlots();

            client.Send(new MapSwapPacket(
                _configuration[GamerServerPublic],
                _configuration[GameServerPort],
                client.Tamer.Location.MapId,
                client.Tamer.Location.X,
                client.Tamer.Location.Y
            ));

            client.Send(new SystemMessagePacket("Evolucoes e montarias restauradas com sucesso!"));
        }




        private async Task ItemFindCommand(GameClient client, string[] command)
        {
            if (command.Length < 2)
            {
                client.Send(new SystemMessagePacket("Usage: !itemfind <ItemName>"));
                return;
            }

            var itemNameQuery = NormalizeString(command[1]);
            _logger.Debug($"Searching for items with normalized name '{itemNameQuery}' in map {client.Tamer.Location.MapId}...");

            var mapShops = await _sender.Send(new ConsignedShopsQuery(client.Tamer.Location.MapId));
            _logger.Debug($"Found {mapShops.Count} shops in the current map.");

            if (mapShops.Count == 0)
            {
                client.Send(new SystemMessagePacket("Nenhuma loja encontrada no seu mapa atual."));
                return;
            }

            var shopOwnersTasks = mapShops.Select(shop => _sender.Send(new CharacterByIdQuery(shop.CharacterId)));
            var shopOwners = await Task.WhenAll(shopOwnersTasks);

            var matchingItems = new List<(string ShopName, string OwnerName, byte Channel, string ItemName, int Amount, long Price)>();

            for (int i = 0; i < mapShops.Count; i++)
            {
                var shop = mapShops[i];
                var shopOwner = _mapper.Map<CharacterModel>(shopOwners[i]);

                if (shopOwner.ConsignedShopItems?.Items == null || !shopOwner.ConsignedShopItems.Items.Any())
                {
                    continue;
                }

                foreach (var item in shopOwner.ConsignedShopItems.Items)
                {
                    var itemName = _assets.ItemInfo.FirstOrDefault(x => x.ItemId == item.ItemId)?.Name ?? "Unknown Item";

                    if (!NormalizeString(itemName).Contains(itemNameQuery))
                        continue;

                    if (item.TamerShopSellPrice <= 0)
                    {
                        _logger.Warning($"Item '{itemName}' in shop '{shop.ShopName}' has invalid price: {item.TamerShopSellPrice}");
                        continue;
                    }

                    matchingItems.Add((
                        ShopName: shop.ShopName,
                        OwnerName: shopOwner.Name,
                        Channel: shop.Channel,
                        ItemName: itemName,
                        Amount: item.Amount,
                        Price: item.TamerShopSellPrice
                    ));
                }
            }

            if (!matchingItems.Any())
            {
                client.Send(new SystemMessagePacket("Nenhum item com esse nome encontrado no seu mapa atual."));
                return;
            }

            matchingItems.Sort((a, b) => a.Price.CompareTo(b.Price));

            const int MaxMessageLength = 250;
            var messages = new List<string>();
            var currentMsg = "Item encontrado (Ordenado por valor):\n";

            foreach (var match in matchingItems)
            {
                string line = $"- {match.ItemName} x{match.Amount} por {FormatCurrency(match.Price)} (Shop: {match.ShopName}, Dono: {match.OwnerName}, Canal: {match.Channel})\n";

                if (currentMsg.Length + line.Length > MaxMessageLength)
                {
                    messages.Add(currentMsg);
                    currentMsg = "";
                }

                currentMsg += line;
            }

            if (!string.IsNullOrEmpty(currentMsg))
            {
                messages.Add(currentMsg);
            }

            foreach (var msg in messages)
            {
                client.Send(new SystemMessagePacket(msg));
            }
        }

        private string FormatCurrency(long price)
        {
            if (price < 1000)
                return $"{price} Bits";

            long trillions = price / 1_000_000;  // Pega a parte de trilhões
            long millions = (price % 1_000_000) / 1000; // Pega a parte de milhões
            long bits = price % 1000; // Pega o restante

            string formatted = "";

            if (trillions > 0)
                formatted += $"{trillions}T";

            if (millions > 0)
                formatted += $"{millions}M";

            if (bits > 0 && trillions == 0) // Só exibir Bits se não houver T
                formatted += $"{bits} Bits";

            return formatted.Trim();
        }


        private string NormalizeString(string input)
        {
            return input
                .Trim()
                .ToLower()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "");
        }



        private async Task CriticalCommand(GameClient client, string[] command)
        {
            var regex = @"^critical\s+(on|off)\s*$";
            var match = Regex.Match(string.Join(" ", command), regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Comando inválido. Use: !critical (on/off)."));
                return;
            }

            bool enable = match.Groups[1].Value.Equals("on", StringComparison.OrdinalIgnoreCase);
            client.EnableCriticalMessages = enable;

            string status = enable ? "habilitadas" : "desabilitadas";
            client.Send(new SystemMessagePacket($"Mensagens de crítico {status}."));
        }
        public async Task ExecuteCommand(GameClient client, string message)
        {
            var command = Regex.Replace(message.Trim().ToLower(), @"\s+", " ").Split(' ');

            if (commands.TryGetValue(command[0], out var commandInfo))
            {
                if (commandInfo.AccessLevels?.Contains(client.AccessLevel) != false)
                {
                    //_logger.Information($"Sending Command!! [PlayerCommand]");
                    await commandInfo.Command(client, command);
                }
                else
                {
                    _logger.Warning($"Tamer {client.Tamer.Name} tryed to use the command {message} without permission !! [PlayerCommand]");
                    client.Send(new SystemMessagePacket("You do not have permission to use this command."));
                }
            }
            else
            {
                client.Send(new SystemMessagePacket($"Invalid Command !!\nType !help"));
            }
        }

        #region Commands

        /*  private async Task ClearCommand(GameClient client, string[] command)
         {
             var regex = @"^clear\s+(inv|cash|gift)$";
             var match = Regex.Match(string.Join(" ", command), regex, RegexOptions.IgnoreCase);

             if (!match.Success)
             {
                 client.Send(new SystemMessagePacket("Unknown command.\nType !clear {inv|cash|gift}\n"));
                 return;
             }

             if (command[1] == "inv")
             {
                 client.Tamer.Inventory.Clear();
                 client.Send(new SystemMessagePacket($" Inventory slots cleaned."));
                 client.Send(new LoadInventoryPacket(client.Tamer.Inventory, InventoryTypeEnum.Inventory));
                 await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
             }
             else if (command[1] == "cash")
             {
                 client.Tamer.AccountCashWarehouse.Clear();
                 client.Send(new SystemMessagePacket($" CashStorage slots cleaned."));
                 client.Send(new LoadAccountWarehousePacket(client.Tamer.AccountCashWarehouse));
                 await _sender.Send(new UpdateItemsCommand(client.Tamer.AccountCashWarehouse));
             }
             else if (command[1] == "gift")
             {
                 client.Tamer.GiftWarehouse.Clear();
                 client.Send(new SystemMessagePacket($" GiftStorage slots cleaned."));
                 client.Send(new LoadGiftStoragePacket(client.Tamer.GiftWarehouse));
                 await _sender.Send(new UpdateItemsCommand(client.Tamer.GiftWarehouse));
             }
         } */

        private async Task BattleLogCommand(GameClient client, string[] command)
        {
            var regex = @"^battlelog\s+(on|off)\s*$";
            var match = Regex.Match(string.Join(" ", command), regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command. Type !battlelog (on/off)."));
                return;
            }

            string action = match.Groups[1].Value.ToLower();

            switch (action)
            {
                case "on":
                    if (!AttackManager.IsBattle)
                    {
                        AttackManager.SetBattleStatus(true);
                        client.Send(new NoticeMessagePacket($"Battle log is now active!"));
                    }
                    else
                    {
                        client.Send(new NoticeMessagePacket($"Battle log is already active..."));
                    }
                    break;

                case "off":
                    if (AttackManager.IsBattle)
                    {
                        AttackManager.SetBattleStatus(false);
                        client.Send(new NoticeMessagePacket($"Battle log is now inactive!"));
                    }
                    else
                    {
                        client.Send(new NoticeMessagePacket($"Battle log is already inactive..."));
                    }
                    break;

                default:
                    client.Send(new SystemMessagePacket($"Invalid command. Use !battlelog (on/off)"));
                    break;
            }
        }

        private async Task StatsCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"^stats\s*$";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !stats"));
                return;
            }

            client.Send(new SystemMessagePacket($"Critical Damage: {client.Tamer.Partner.CD / 100}%\n" +
                $"Attribute Damage: {client.Tamer.Partner.ATT / 100}%\n" +
                $"Digimon SKD: {client.Tamer.Partner.SKD}\n" +
                $"Digimon SCD: {client.Tamer.Partner.SCD / 100}%\n" +
                $"Tamer BonusEXP: {client.Tamer.BonusEXP}%\n" +
                $"Tamer Move Speed: {client.Tamer.MS}"));
        }

        private async Task TimeCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"^time\s*$";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !time"));
                return;
            }

            client.Send(new SystemMessagePacket($"Server Time is: {DateTime.UtcNow}"));
        }

        private async Task DeckLoadCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"^deckload\s*$";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !deckload"));
                return;
            }

            var allDigimons = client.Tamer.Digimons.ToList();
            var encyclopediaMap = client.Tamer.Encyclopedia.ToDictionary(e => e.DigimonEvolutionId, e => e);

            // Verifica se todos os Digimons já estão atualizados na enciclopédia
            bool hasNewData = false;

            foreach (var digimon in allDigimons)
            {
                var digimonEvolutionInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == digimon.BaseType);
                if (digimonEvolutionInfo == null)
                    continue;

                if (!encyclopediaMap.TryGetValue(digimonEvolutionInfo.Id, out var encyclopedia))
                {
                    hasNewData = true;
                    break;
                }

                foreach (var evolution in digimon.Evolutions)
                {
                    var existingEvolution = encyclopedia.Evolutions
                        .FirstOrDefault(x => x.DigimonBaseType == evolution.Type);

                    if (existingEvolution == null || (!existingEvolution.IsUnlocked && evolution.Unlocked == 1))
                    {
                        hasNewData = true;
                        break;
                    }
                }

                if (hasNewData)
                    break;
            }

            if (!hasNewData)
            {
                client.Send(new SystemMessagePacket("Todos os Digimons ja estao atualizados na enciclopedia.\nEquipe apenas os Digimons que ainda nao foram registrados."));
                return;
            }

            int updatedCount = 0;

            foreach (var digimon in allDigimons)
            {
                var digimonEvolutionInfo = _assets.EvolutionInfo.FirstOrDefault(x => x.Type == digimon.BaseType);
                if (digimonEvolutionInfo == null)
                    continue;

                if (!encyclopediaMap.TryGetValue(digimonEvolutionInfo.Id, out var encyclopedia))
                {
                    // Criar nova entrada
                    var newEncyclopedia = CharacterEncyclopediaModel.Create(
                        client.TamerId,
                        digimonEvolutionInfo.Id,
                        digimon.Level,
                        digimon.Size,
                        0, 0, 0, 0, 0,
                        false, false
                    );

                    digimon.Evolutions?.ForEach(x =>
                    {
                        var evolutionLine = digimonEvolutionInfo.Lines.FirstOrDefault(y => y.Type == x.Type);
                        byte slotLevel = evolutionLine?.SlotLevel ?? 0;
                        newEncyclopedia.Evolutions.Add(
                            CharacterEncyclopediaEvolutionsModel.Create(newEncyclopedia.Id, x.Type, slotLevel, Convert.ToBoolean(x.Unlocked))
                        );
                    });

                    var encyclopediaAdded = await _sender.Send(new CreateCharacterEncyclopediaCommand(newEncyclopedia));
                    client.Tamer.Encyclopedia.Add(encyclopediaAdded);
                    encyclopediaMap[digimonEvolutionInfo.Id] = encyclopediaAdded;
                    updatedCount++;
                }
                else
                {
                    // Atualizar evoluções existentes ou adicionar novas
                    bool updated = false;

                    foreach (var evolution in digimon.Evolutions)
                    {
                        var encyclopediaEvolution = encyclopedia.Evolutions
                            .FirstOrDefault(x => x.DigimonBaseType == evolution.Type);

                        if (encyclopediaEvolution != null)
                        {
                            if (!encyclopediaEvolution.IsUnlocked && evolution.Unlocked == 1)
                            {
                                encyclopediaEvolution.Unlock();
                                await _sender.Send(new UpdateCharacterEncyclopediaEvolutionsCommand(encyclopediaEvolution));
                                updated = true;
                            }
                        }
                        else
                        {
                            var evoInfo = digimonEvolutionInfo.Lines.FirstOrDefault(x => x.Type == evolution.Type);
                            byte slotLevel = evoInfo?.SlotLevel ?? 0;

                            var newEvolution = CharacterEncyclopediaEvolutionsModel.Create(
                                encyclopedia.Id, evolution.Type, slotLevel, Convert.ToBoolean(evolution.Unlocked));

                            encyclopedia.Evolutions.Add(newEvolution);
                            updated = true;
                        }
                    }

                    if (encyclopedia.Evolutions.All(x => x.IsUnlocked))
                    {
                        encyclopedia.SetRewardAllowed();
                        updated = true;
                    }

                    if (updated)
                    {
                        await _sender.Send(new UpdateCharacterEncyclopediaCommand(encyclopedia));
                        updatedCount++;
                    }
                }
            }

            client.Send(new SystemMessagePacket($"Encyclopedia verificada e atualizada para todos os Digimons! ({updatedCount} atualizacoes)"));
        }

        private async Task PvpCommand(GameClient client, string[] command)
        {
            string message = string.Join(" ", command);

            var regex = @"pvp\s+(on|off)";
            var match = Regex.Match(message, regex, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                client.Send(new SystemMessagePacket($"Unknown command.\nType !pvp (on/off)"));
                return;
            }

            if (client.Tamer.InBattle)
            {
                client.Send(new SystemMessagePacket($"You can't turn off pvp on battle !"));
                return;
            }

            string action = match.Groups[1].Value.ToLower();

            switch (action)
            {
                case "on":
                    {
                        if (client.Tamer.PvpMap == false)
                        {
                            client.Tamer.PvpMap = true;
                            client.Send(new NoticeMessagePacket($"PVP turned on !!"));
                        }
                        else
                        {
                            client.Send(new NoticeMessagePacket($"PVP is already on ..."));
                        }
                    }
                    break;

                case "off":
                    {
                        if (client.Tamer.PvpMap == true)
                        {
                            client.Tamer.PvpMap = false;
                            client.Send(new NoticeMessagePacket($"PVP turned off !!"));
                        }
                        else
                        {
                            client.Send(new NoticeMessagePacket($"PVP is already off ..."));
                        }
                    }
                    break;
            }
        }

        // --- HELP ---------------------------------------------------------------
        private async Task HelpCommand(GameClient client, string[] command)
        {
            if (command[1] == "inv")
            {
                client.Send(new SystemMessagePacket("!clear inv: Clear your inventory"));
            }
            else if (command[1] == "cash")
            {
                client.Send(new SystemMessagePacket("!clear cash: Clear your CashStorage"));
            }
            else if (command[1] == "timeboss")
            {
                client.Send(new SystemMessagePacket("!timeboss use para ver o horario que os boss nasceram no mapa"));
            }
            else if (command[1] == "gift")
            {
                client.Send(new SystemMessagePacket("!clear gift: Clear your GiftStorage"));
            }
            else if (command[1] == "stats")
            {
                client.Send(new SystemMessagePacket("!stats: Show hidden stats"));
            }
            else if (command[1] == "time")
            {
                client.Send(new SystemMessagePacket("!time: Show the server time"));
            }
            else if (command[1] == "Done")
            {
                client.Send(new SystemMessagePacket("!Done: is used to sacrifise the digimon you want to get ruin item"));
            }
            else
            {
                client.Send(new SystemMessagePacket("Commands:\n1. !clear\n2. !stats\n3. !time\nType !help {command} for more details."));
            }
        }

        #endregion

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
