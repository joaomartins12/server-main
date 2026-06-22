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
    public partial class ContainerUpdate
    {
        private MudAutocomplete<ItemAssetViewModel> _selectedItemAsset;

        ContainerViewModel _container = new ContainerViewModel();
        bool Loading = false;
        long _id;

        [Parameter]
        public string ContainerId { get; set; }

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

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            if (long.TryParse(ContainerId, out _id))
            {

                var target = await Sender.Send(
                    new GetContainerByIdQuery(_id)
                );

                if (target.Register == null)
                    _id = 0;
                else
                {
                    _container = Mapper.Map<ContainerViewModel>(target.Register);

                    foreach (var reward in _container.Rewards)
                    {
                        var itemInfoQuery = await Sender.Send(new GetItemAssetByIdQuery(reward.ItemId));
                        reward.ItemInfo = Mapper.Map<ItemAssetViewModel>(itemInfoQuery.Register);
                    }
                }
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);

            if (_id == 0)
            {
                Toast.Add("Container config not found, try again later.", Severity.Warning);

                Return();
            }

            StateHasChanged();
        }

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
            var remove = _container.Rewards.FirstOrDefault(x => x.Id == id);
            _container.Rewards.Remove(remove);

            StateHasChanged();
        }

        public async Task CallDiscord(string message, GameClient tamer, string coloured, string local, string Channel = "1374526322776215703", bool custom = false)
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

        private async Task Update()
        {
            try
            {
                if (_container.Rewards.Any(x => x.Invalid))
                {
                    Toast.Add("Invalid container configuration.", Severity.Warning);
                    return;
                }

                Loading = true;
                StateHasChanged();

                // Captura o estado anterior do container
                var previousContainerState = Mapper.Map<ContainerAssetDTO>(_container);

                _container.Rewards.RemoveWhere(x => x.ItemInfo == null);

                foreach (var reward in _container.Rewards)
                {
                    reward.ItemId = reward.ItemInfo.ItemId;
                    reward.ItemName = reward.ItemInfo.Name;
                }

                var containerDto = Mapper.Map<ContainerAssetDTO>(_container);

                // Log mudanças para o Discord
                var changes = new StringBuilder();
                changes.AppendLine("Container configuration updated:");
                changes.AppendLine($"Container ID: {_container.Id}");
                changes.AppendLine($"Reward Count (Before): {previousContainerState.Rewards.Count}"); // Adicionado o count de rewards antes
                changes.AppendLine("Previous Rewards:");
                foreach (var reward in previousContainerState.Rewards)
                {
                    changes.AppendLine($"- Item ID: {reward.ItemId}, Item Name: {reward.ItemName} quantidade: {reward.MinAmount}, {reward.MaxAmount} e chance {reward.Chance}");
                }

                changes.AppendLine("Updated Rewards:");
                foreach (var reward in _container.Rewards)

                {
                    changes.AppendLine($"- Item ID: {reward.ItemId}, Item Name: {reward.ItemName}, quantidade: {reward.MinAmount}, {reward.MaxAmount} e chance {reward.Chance}");

                }

                // Obtém o usuário atual
                var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
                var user = authState.User.Identity?.Name ?? "Unknown";

                changes.AppendLine($"Updated by: {user}");

                // Envia log para o Discord
                await CallDiscord(
                    message: changes.ToString(),
                    tamer: null,
                    coloured: "00FF00", // Cor verde
                    local: "ContainerUpdate",
                    Channel: "1374553162278506617",
                    custom: true
                );

                await Sender.Send(new UpdateContainerConfigCommand(containerDto));

                Toast.Add("Container config updated successfully.", Severity.Success);
                Return();
            }
            catch (Exception ex)
            {
                Logger.Error("Error updating container config with id {id}: {ex}", _container.Id, ex.Message);
                Toast.Add("Unable to update container config, try again later.", Severity.Error);
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