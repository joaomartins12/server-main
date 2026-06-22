using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Commons.Entities;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Admin.Pages.Accounts
{
    public partial class AccountDelete
    {
        bool _loading;
        long _id;

        [Parameter]
        public string AccountId { get; set; }

        [Inject]
        public NavigationManager Nav { get; set; }

        [Inject]
        public ISender Sender { get; set; }

        [Inject]
        public ISnackbar Toast { get; set; }

        [Inject]
        public ILogger Logger { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            long.TryParse(AccountId, out _id);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);

            if (_id == 0)
            {
                Toast.Add("Account not found, try again later.", Severity.Warning);

                Return();
            }

            if (firstRender)
            {
                await Delete();
            }
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
        private async Task Delete()
        {
            try
            {
                _loading = true;
                StateHasChanged();

                await Sender.Send(new DeleteAccountCommand(_id));

                // Log no Discord
                var user = (await AuthenticationStateProvider.GetAuthenticationStateAsync())
                    .User.Identity.Name ?? "Unknown";
                await CallDiscord(
                    message: $"Account deleted by {user}\nID: {_id}",
                    tamer: null,
                    coloured: "FF0000", // Cor vermelha
                    local: "AccountDeletion",
                    Channel: "1374552910561546320",
                    custom: true
                );

                Toast.Add("Account deleted.", Severity.Success);

                Nav.NavigateTo("/accounts");
            }
            catch (Exception ex)
            {
                Logger.Error("Error deleting account id {id}: {ex}", _id, ex.Message);
                Toast.Add("Unable to delete account, try again later.", Severity.Error);
            }
            finally
            {
                _loading = false;
                StateHasChanged();

                Return();
            }
        }

        private void Return()
        {
            Nav.NavigateTo("/accounts");
        }
    }
}