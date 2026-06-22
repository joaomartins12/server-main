using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.ViewModel.Account;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using DigitalWorldOnline.Commons.Extensions;
using MudBlazor;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Packets.GameServer;

namespace DigitalWorldOnline.Admin.Pages.Accounts
{
    public partial class AccountUpdate
    {
        AccountUpdateViewModel _account = new();
        bool _loading = true;
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

        private DateTime _banStart = DateTime.UtcNow;
        private DateTime _banEnd = DateTime.UtcNow.AddDays(7);

        private bool _showBanDialog = false;
        private DateTime? _banStartDate = DateTime.Now;
        private DateTime? _banEndDate;
        private string _banReason = string.Empty;
        // Pseudocódigo detalhado para integração do método BanAccount
        // 1. Validar se todos os campos obrigatórios do banimento estão preenchidos (_account.BlockType, _account.BlockReason, _account.BlockStartDate, _account.BlockEndDate se não for permanente).
        // 2. Se faltar algum campo, exibir Toast de aviso e retornar.
        // 3. Definir endDate como DateTime.MaxValue se o banimento for permanente, senão usar _account.BlockEndDate.
        // 4. Enviar comando BanAccountCommand via Sender com os dados necessários.
        // 5. Exibir Toast de sucesso.
        // 6. Chamar CallDiscord para notificar o banimento no Discord, formatando a mensagem conforme os dados do banimento.
        // 7. Em caso de exceção, logar o erro e exibir Toast de erro.

        private async Task BanAccount()
        {
            // Validação dos campos obrigatórios para banimento
            if (_account.BlockType == null
                || string.IsNullOrWhiteSpace(_account.BlockReason)
                || !_account.BlockStartDate.HasValue
                || (_account.BlockType != AccountBlockEnum.Permanent && !_account.BlockEndDate.HasValue))
            {
                Toast.Add("Preencha todos os campos obrigatórios do banimento.", Severity.Warning);
                return;
            }

            try
            {
                // Define a data final: se permanente, usa DateTime.MaxValue
                var endDate = _account.BlockType == AccountBlockEnum.Permanent
                    ? DateTime.MaxValue
                    : _account.BlockEndDate!.Value;

                // Envia o comando de banimento
                await Sender.Send(new BanAccountCommand(
                    accountId: _account.Id,
                    type: _account.BlockType.Value,
                    reason: _account.BlockReason,
                    startDate: _account.BlockStartDate.Value,
                    endDate: endDate
                ));

                // Força o disconnect do jogador se estiver online
                try
                {
                    var gameServer = (GameServer)AppDomain.CurrentDomain.GetData("GameServerInstance");

                    if (gameServer != null)
                    {
                        var client = gameServer.FindByAccountId(_account.Id);
                        if (client != null)
                        {
                            // Calcula o tempo restante
                            var now = DateTime.UtcNow;
                            var secondsRemaining = (uint)Math.Max(0, (endDate - now).TotalSeconds);

                            // Envia o pacote de banimento com motivo e tempo restante
                            client.Send(new BanUserPacket(secondsRemaining, _account.BlockReason));

                            // Pequeno delay para garantir envio do pacote
                            await Task.Delay(300);

                            // Desconecta o jogador
                            // Se existir método Disconnect público, chame diretamente, senão use reflection
                            var disconnectMethod = client.GetType().GetMethod("Disconnect");
                            if (disconnectMethod != null)
                            {
                                disconnectMethod.Invoke(client, null);
                            }
                            else
                            {
                                // Alternativa: remova o client do servidor
                                gameServer.Disconnect(client, true);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning("Não foi possível forçar o disconnect do jogador {Id}: {Message}", _account.Id, ex.Message);
                }

                Toast.Add("Conta banida com sucesso.", Severity.Success);

                // Mensagem para o Discord
                var endDateText = endDate == DateTime.MaxValue
                    ? "Permanente"
                    : endDate.ToString("dd/MM/yyyy HH:mm");

                var discordMessage =
                    $"Conta **{_account.Username}** foi banida\n" +
                    $"🛑 Tipo: {_account.BlockType}\n" +
                    $"📅 Início: {_account.BlockStartDate.Value:dd/MM/yyyy HH:mm}\n" +
                    $"📅 Fim: {endDateText}\n" +
                    $"📄 Motivo: {_account.BlockReason}";

                await CallDiscord(
                    message: discordMessage,
                    tamer: null,
                    coloured: "FF0000",
                    local: "AccountBan",
                    Channel: "1374551861683683338",
                    custom: true
                );
            }
            catch (Exception ex)
            {
                Logger.Error("Erro ao banir conta {Id}: {Message}", _account.Id, ex.Message);
                Toast.Add("Erro ao banir a conta.", Severity.Error);
            }
        }




        // Assume que _account é o modelo da conta carregado no componente
        // e que possui as propriedades para bloqueio.


        // Propriedade para saber se a conta está banida atualmente
        private bool _isBanned => _account.BlockType != null;

        private bool _showUnbanDialog = false;


        // Variáveis para controle dos diálogos



        // Propriedade para verificar se a conta está banida


        // Método de desbanimento (já existente no seu código)

        private async Task UnbanAccount()
        {
            try
            {
                _loading = true;
                StateHasChanged();

                await Sender.Send(new UnbanAccountCommand(_account.Id));

                // Atualiza os campos locais
                _account.BlockType = null;
                _account.BlockReason = null;
                _account.BlockStartDate = null;
                _account.BlockEndDate = null;

                Toast.Add("Conta desbanida com sucesso!", Severity.Success);

                // Notificação no Discord
                await CallDiscord(
                    message: $"Conta **{_account.Username}** foi desbanida ✅",
                    tamer: null,
                    coloured: "00AA00",
                    local: "AccountUnban",
                    Channel: "1402368661687373956",
                    custom: true
                );

                _showUnbanDialog = false;
            }
            catch (Exception ex)
            {
                Logger.Error("Erro ao desbanir conta {Id}: {Message}", _account.Id, ex.Message);
                Toast.Add("Erro ao desbanir a conta.", Severity.Error);
            }
            finally
            {
                _loading = false;
                StateHasChanged();
            }
        }



        private bool IsUnbanButtonDisabled => (_account?.BlockType == null) || _loading;

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            if (long.TryParse(AccountId, out _id))
            {
                var target = await Sender.Send(
                    new GetAccountByIdQuery(_id)
                );

                _account = target.Register != null
               ? new AccountUpdateViewModel(
                   target.Register.Id,
                   target.Register.Username,
                   target.Register.Email,
                   target.Register.AccessLevel,
                   target.Register.Premium,
                   target.Register.Silk,
                   target.Register.DiscordId,
                   //  target.Register.Password,
                   target.Register.AccountBlock?.Type, // Corrigido de BlockType para Type
                   target.Register.AccountBlock?.Reason ?? string.Empty,
                   target.Register.AccountBlock?.StartDate,
                   target.Register.AccountBlock?.EndDate
               )
               : null;

            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);

            if (_id == 0 || _account == null)
            {
                Toast.Add("Account not found, try again later.", Severity.Warning);

                Return();
            }

            if (firstRender)
            {
                _loading = false;
                StateHasChanged();
            }
        }

        public async Task CallDiscord(string message, GameClient tamer, string coloured, string local, string Channel = "1374551248061202632", bool custom = false)
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
            if (_account.Empty)
                return;

            // Validação do DiscordId: pode conter 1 dígito, ou entre 15 e 20 dígitos
            if (!string.IsNullOrEmpty(_account.DiscordId) &&
                !(_account.DiscordId.Length == 1 || (_account.DiscordId.Length >= 15 && _account.DiscordId.Length <= 20)))
            {
                Toast.Add("O Discord ID deve conter 1 dígito ou entre 15 e 20 dígitos.", Severity.Warning);
                return;
            }

            try
            {
                _loading = true;
                StateHasChanged();

                // Buscar os dados atuais da conta antes do update
                var original = await Sender.Send(new GetAccountByIdQuery(_account.Id));

                  await Sender.Send(
                  new UpdateAccountCommand(
                      _account.Id,
                      _account.Username,
                      _account.Email,
                      _account.AccessLevel,
                      _account.Premium,
                      _account.Silk,
                      string.IsNullOrWhiteSpace(_account.Password) ? null : _account.Password.Encrypt(),
                      _account.DiscordId
                  )
              );


                var updater = (await AuthenticationStateProvider.GetAuthenticationStateAsync())
                    .User.Identity.Name ?? "Unknown";

                await CallDiscord(
                    message: $"Account updated by {updater}\n" +
                             $"**Before Update:**\n" +
                             $"Username: {original.Register?.Username}\n" +
                             $"Email: {original.Register?.Email}\n" +
                             $"Access Level: {original.Register?.AccessLevel}\n" +
                             $"Premium: {original.Register?.Premium}\n" +
                             $"Silk: {original.Register?.Silk}\n\n" +
                             $"**After Update:**\n" +
                             $"Username: {_account.Username}\n" +
                             $"Email: {_account.Email}\n" +
                             $"Access Level: {_account.AccessLevel}\n" +
                             $"Premium: {_account.Premium}\n" +
                             $"Silk: {_account.Silk}",
                    tamer: null,
                    coloured: "FFFF00",
                    local: "AccountUpdate",
                    Channel: "1374553001796046968",
                    custom: true
                );

                Toast.Add("Account updated.", Severity.Success);
                Nav.NavigateTo("/accounts");
            }
            catch (Exception ex)
            {
                Logger.Error("Error updating account id {id}: {ex}", _account.Id, ex.Message);
                Toast.Add("Unable to update account, try again later.", Severity.Error);
            }
            finally
            {
                _loading = false;
                StateHasChanged();
            }
        }

        private void Return()
        {
            Nav.NavigateTo("/accounts");
        }
    }
}