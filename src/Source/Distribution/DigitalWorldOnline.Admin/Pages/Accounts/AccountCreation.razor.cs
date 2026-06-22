using AutoMapper;
using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Commons.DTOs.Account;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Models.Account;
using DigitalWorldOnline.Commons.ViewModel.Account;
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
    public partial class AccountCreation
    {
        bool Loading = false;

        AccountCreationViewModel _account = new AccountCreationViewModel();

        [Inject]
        public NavigationManager Nav { get; set; }

        [Inject]
        public ISender Sender { get; set; }

        [Inject]
        public ISnackbar Toast { get; set; }

        [Inject]
        public IMapper Mapper { get; set; }

        [Inject]
        public ILogger Logger { get; set; }

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
            // Torna obrigatório o preenchimento do DiscordId
            if (_account.Empty || string.IsNullOrWhiteSpace(_account.DiscordId))
            {
                Toast.Add("O campo Discord ID é obrigatório.", Severity.Error);
                return;
            }

            try
            {
                Loading = true;

                StateHasChanged();

                // Criação do modelo de conta
                var account = AccountModel.Create(
                    _account.Username,
                    _account.Password.Encrypt(),
                    _account.Email,
                    _account.DiscordId, // novo campo
                    null,
                    _account.AccessLevel,
                    _account.Premium,
                    _account.Silk);

                var accountDto = Mapper.Map<AccountDTO>(account);

                // Envia o comando para criar a conta
                await Sender.Send(new CreateAccountCommand(accountDto));

                // Log no Discord
                var creator = (await AuthenticationStateProvider.GetAuthenticationStateAsync())
                   .User.Identity.Name ?? "Unknown";
                await CallDiscord(
                    message: $"Account created by {creator}\n: {_account.Username}\n" +
                             $"Email: {_account.Email}\n" +
                             $"Access Level: {_account.AccessLevel}\n" +
                             $"Premium: {_account.Premium}\n" +
                             $"Silk: {_account.Silk}",
                    tamer: null,
                    coloured: "00FF00", // Cor verde
                    local: "Criação De Conta",
                    Channel: "1374552808938016902",
                    custom: true
                );

                Toast.Add("Account created successfully.", Severity.Success);

                Nav.NavigateTo("/accounts");
            }
            catch (Exception ex)
            {
                Logger.Error("Error creating account with username {name}: {ex}", _account.Username, ex.Message);
                Toast.Add("Unable to create account, try again later.", Severity.Error);
            }
            finally
            {
                Loading = false;

                StateHasChanged();
            }
        }

        private void Return()
        {
            Nav.NavigateTo("/accounts");
        }
    }
}