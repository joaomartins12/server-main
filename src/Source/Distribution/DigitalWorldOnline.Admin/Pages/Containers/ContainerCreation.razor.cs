using AutoMapper;
using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.ViewModel.Asset;
using DigitalWorldOnline.Commons.ViewModel.Containers;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Admin.Pages.Containers
{
    public partial class ContainerCreation
    {
        private MudAutocomplete<ItemAssetViewModel> _selectedItemAsset;

        ContainerViewModel _container = new ContainerViewModel();

        bool Loading = false;

        [Inject]
        public NavigationManager Nav { get; set; }

        [Inject]
        public ISender Sender { get; set; }

        [Inject]
        public IMapper Mapper { get; set; }

        [Inject]
        public ISnackbar Toast { get; set; }

        [Inject]
        public ILogger Logger { get; set; }

        private async Task<IEnumerable<ItemAssetViewModel>> GetItemAssets(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length < 3)
            {
                if (string.IsNullOrEmpty(value))
                {
                    await _selectedItemAsset.Clear();
                }

                return new ItemAssetViewModel[0];
            }

            var assets = await Sender.Send(new GetItemAssetQuery(value));

            return Mapper.Map<List<ItemAssetViewModel>>(assets.Registers);
        }

        private void AddReward()
        {
            if (_container.Rewards.Any())
                _container.Rewards.AddFirst(ContainerRewardViewModel.Create(_container.Rewards.Max(x => x.Id)));
            else
                _container.Rewards.AddFirst(new ContainerRewardViewModel());

            StateHasChanged();
        }

        private void DeleteReward(long id)
        {
            _container.Rewards.RemoveWhere(x => x.Id == id);

            StateHasChanged();
        }

        public async Task CallDiscord(string message, GameClient tamer, string coloured, string local, string Channel = "1307382358084944075", bool custom = false)
        {
            var myChannel = Channel;
            var myToken = "MTA3NzM0OTg1NDI4MTQ3NDA5MA.GBj8pg.cnzts2HQklQ4Mc9nbwBS1YvMOzIBZ3yvRYnjyk";

            var payload = new
            {
                tts = false,
                embeds = new[]
                {
                    new
                    {
                        type = "rich",
                        color = Convert.ToInt32(coloured, 16),
                        footer = new
                        {
                            text = custom
                            ? $"{message}"
                            : $"[ACC {tamer.AccountId}][{local}][CH {tamer.Tamer.Channel}] -> {tamer.Tamer.Name}: {message}"
                        },
                    }
                }
            };

            var json_data = JsonConvert.SerializeObject(payload);

            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri($"https://discordapp.com/api/v6/channels/{myChannel}/messages"),
                    Content = new StringContent(json_data, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("Authorization", $"Bot {myToken}");

                var response = await client.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

            }
        }

        public async Task CallDiscordWarnings(string title, string message, string coloured, string dischannel, string role, long digimonid)
        {
            var payload = new
            {
                title = title,
                message = message,
                coloured = coloured,
                dischannel = dischannel,
                role = role,
                digimonid = digimonid,
                type = 1
            };

            var json_data = JsonConvert.SerializeObject(payload);

            using (var client = new HttpClient())
            {
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri("http://admin.mundodigitaluniverse.space/discord.php"),
                    Content = new StringContent(json_data, Encoding.UTF8, "application/json")
                };

                var response = await client.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();
            }
        }

        [Inject]
        public AuthenticationStateProvider AuthenticationStateProvider { get; set; }

        private async Task Create()
        {
            try
            {
                if (_container.Invalid || _container.Rewards.Any(x => x.Invalid))
                {
                    Toast.Add("Invalid container configuration.", Severity.Warning);
                    return;
                }

                Loading = true;
                StateHasChanged();

                _container.Rewards.RemoveWhere(x => x.ItemInfo == null);

                foreach (var reward in _container.Rewards)
                {
                    reward.Id = 0;
                    reward.ItemId = reward.ItemInfo.ItemId;
                    reward.ItemName = reward.ItemInfo.Name;
                }

                _container.ItemId = _container.ItemInfo.ItemId;
                _container.ItemName = _container.ItemInfo.Name;

                var newContainer = Mapper.Map<ContainerAssetDTO>(_container);

                await Sender.Send(new CreateContainerConfigCommand(newContainer));

                // Log no Discord
                var creator = (await AuthenticationStateProvider.GetAuthenticationStateAsync())
                    .User.Identity.Name ?? "Unknown";

                var rewardList = string.Join("\n", _container.Rewards.Select(r =>
              $"- {r.ItemName} (ID: {r.ItemId}) x{r.MinAmount}-{r.MaxAmount}"));

                await CallDiscord(
                    message: $"Container config created by {creator}\n" +
                             $"Container Item: {_container.ItemName} (ID: {_container.ItemId})\n" +
                             $"Rewards:\n{rewardList}",
                    tamer: null,
                    coloured: "00FFFF", // Cor ciano
                    local: "ContainerCreation",
                    Channel: "1374553098793517096",
                    custom: true
                );

                Toast.Add("Container config created successfully.", Severity.Success);
                Return();
            }
            catch (Exception ex)
            {
                Logger.Error("Error creating container config for item {itemid}: {ex}", _container.ItemId, ex.Message);
                Toast.Add("Unable to create container config, try again later.", Severity.Error);
                Return();
            }
            finally
            {
                Loading = false;
                StateHasChanged();
            }
        }

        private void Return()
        {
            Nav.NavigateTo($"/containers");
        }
    }
}