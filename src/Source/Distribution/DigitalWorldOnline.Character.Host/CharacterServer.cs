using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Packets.CharacterServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Text.Json;
using System.Text;

namespace DigitalWorldOnline.Character
{
    public sealed class CharacterServer : GameServer, IHostedService
    {
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly IConfiguration _configuration;
        private readonly IProcessor _processor;
        private readonly ILogger _logger;

        private const int OnConnectEventHandshakeHandler = 65535;

        public CharacterServer(IHostApplicationLifetime hostApplicationLifetime,
            IConfiguration configuration,
            IProcessor processor,
            ILogger logger)
        {
            OnConnect += OnConnectEvent;
            OnDisconnect += OnDisconnectEvent;
            DataReceived += OnDataReceivedEvent;

            _hostApplicationLifetime = hostApplicationLifetime;
            _configuration = configuration;
            _processor = processor;
            _logger = logger;
        }

        /// <summary>
        /// Event triggered everytime that a game client connects to the server.
        /// </summary>
        /// <param name="sender">The object itself</param>
        /// <param name="gameClientEvent">Game client who connected</param>
        private void OnConnectEvent(object sender, GameClientEvent gameClientEvent)
        {
            var clientIpAddress = gameClientEvent.Client.ClientAddress.Split(':')?.FirstOrDefault();

            //if (InvalidConnection(clientIpAddress))
            //{
            //    _logger.Information($"Blocked connection event from {gameClientEvent.Client.HiddenAddress}. Blocked Addresses: {RefusedAddresses.Count}");

            //    if (!string.IsNullOrEmpty(clientIpAddress) && !RefusedAddresses.Contains(clientIpAddress))
            //        RefusedAddresses.Add(clientIpAddress);

            //    gameClientEvent.Client.Disconnect();
            //    RemoveClient(gameClientEvent.Client);
            //}
            //else
            //{
            //    _logger.Information($"Accepted connection event from {gameClientEvent.Client.HiddenAddress}.");

            //    gameClientEvent.Client.SetHandshake((short)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() & OnConnectEventHandshakeHandler));

            //    if (gameClientEvent.Client.IsConnected)
            //    {
            //        _logger.Debug($"Sending handshake for request source {gameClientEvent.Client.ClientAddress}.");
            //        gameClientEvent.Client.Send(new OnConnectEventConnectionPacket(gameClientEvent.Client.Handshake));
            //    }
            //    else
            //        _logger.Warning($"Request source {gameClientEvent.Client.ClientAddress} has been disconnected.");
            //}

            _logger.Information($"Accepted connection event from {gameClientEvent.Client.HiddenAddress}.");

            gameClientEvent.Client.SetHandshake((short)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() & OnConnectEventHandshakeHandler));

            if (gameClientEvent.Client.IsConnected)
            {
                _logger.Debug($"Sending handshake for request source {gameClientEvent.Client.ClientAddress}.");
                gameClientEvent.Client.Send(new OnConnectEventConnectionPacket(gameClientEvent.Client.Handshake));
            }
            else
                _logger.Warning($"Request source {gameClientEvent.Client.ClientAddress} has been disconnected.");

        }

        /// <summary>
        /// Event triggered everytime the game client disconnects from the server.
        /// </summary>
        /// <param name="sender">The object itself</param>
        /// <param name="gameClientEvent">Game client who disconnected</param>
        private void OnDisconnectEvent(object sender, GameClientEvent gameClientEvent)
        {
            if (!string.IsNullOrEmpty(gameClientEvent.Client.ClientAddress))
            {
                _logger.Information($"Received disconnection event for {gameClientEvent.Client.HiddenAddress}.");
                _logger.Debug($"Source disconnected: {gameClientEvent.Client.ClientAddress}. Account: {gameClientEvent.Client.AccountId}.");
            }
        }

        /// <summary>
        /// Event triggered everytime the game client sends a TCP packet.
        /// </summary>
        /// <param name="sender">The object itself</param>
        /// <param name="gameClientEvent">Game client who sent the packet</param>
        /// <param name="data">The packet content, in byte array</param>
        private void OnDataReceivedEvent(object sender, GameClientEvent gameClientEvent, byte[] data)
        {
            try
            {
                _logger.Debug($"Received {data.Length} bytes from {gameClientEvent.Client.ClientAddress}.");
                _processor.ProcessPacketAsync(gameClientEvent.Client, data);
            }
            catch (Exception ex)
            {
                gameClientEvent.Client.SetGameQuit(true);
                gameClientEvent.Client.Disconnect();

                _logger.Error($"Process packet error: {ex.Message} {ex.InnerException} {ex.StackTrace}.");

                try
                {
                    var filePath = $"PacketErrors/{gameClientEvent.Client.ClientAddress}_{DateTime.Now}.txt";

                    using var fs = File.Create(filePath);
                    fs.Write(data, 0, data.Length);
                }
                catch { }

                //TODO: Salvar no banco com os parametros
            }
        }

        /// <summary>
        /// The default hosted service "starting" method.
        /// </summary>
        /// <param name="cancellationToken">Control token for the operation</param>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.Information($"Starting {GetType().Name}...");

            Console.Title = $"DMO - {GetType().Name}";

            //bool isAuthorized = await CheckAuthorizationAsync();
            //if (!isAuthorized)
            //{
            //    Environment.Exit(1);
            //}

            _hostApplicationLifetime.ApplicationStarted.Register(OnStarted);
            _hostApplicationLifetime.ApplicationStopping.Register(OnStopping);
            _hostApplicationLifetime.ApplicationStopped.Register(OnStopped);

        }

        private async Task<bool> CheckAuthorizationAsync()
        {
            try
            {
                using var httpClient = new HttpClient();
                var payload = JsonSerializer.Serialize(new { GetType().Name });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("http://admin.mundodigitaluniverse.space/api.php", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                var jsonResponse = JsonSerializer.Deserialize<AuthResponse>(responseBody);
                return jsonResponse?.allow == true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private class AuthResponse
        {
            public bool allow { get; set; }
        }
        /// <summary>
        /// The default hosted service "stopping" method
        /// </summary>
        /// <param name="cancellationToken">Control token for the operation</param>
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <summary>
        /// The default hosted service "started" method action
        /// </summary>
        private void OnStarted()
        {
            if (!Listen(_configuration[CharacterServerAddress],
                _configuration[CharacterServerPort],
                _configuration[CharacterServerBacklog]))
            {
                _logger.Error("Unable to start. Check the binding configurations.");
                _hostApplicationLifetime.StopApplication();
                return;
            }

            _logger.Information($"{GetType().Name} started.");
        }

        /// <summary>
        /// The default hosted service "stopping" method action
        /// </summary>
        private void OnStopping()
        {
            _logger.Information($"Disconnecting clients from {GetType().Name}...");

            Shutdown();
        }

        /// <summary>
        /// The default hosted service "stopped" method action
        /// </summary>
        private void OnStopped()
        {
            _logger.Information($"{GetType().Name} stopped.");
        }
        private void LogMessage(ConsoleColor color, string message)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;

            Console.WriteLine("|----------------------------------------------------|");
            Console.WriteLine("|                                                    |");
            Console.WriteLine("|               ██████   ████████  ██    ██          |");
            Console.WriteLine("|               ██   ██     ██     ██    ██          |");
            Console.WriteLine("|               ██   ██     ██     ██    ██          |");
            Console.WriteLine("|               ██   ██     ██     ██    ██          |");
            Console.WriteLine("|               ██████      ██      ██████           |");
            Console.WriteLine("|                                                    |");
            Console.WriteLine("|----------------------------------------------------|");

            // Exibe a história (centralizada dentro de 52 caracteres)
            PrintCenteredLine("Um novo desafio se aproxima...");
            PrintCenteredLine("As trevas emergem das profundezas digitais.");
            PrintCenteredLine("Somente os mais fortes sobreviverão.");
            PrintCenteredLine("A jornada começa agora.");

            Console.WriteLine("|                                                    |");
            Console.WriteLine("|----------------------------------------------------|");

            // Mensagem personalizada
            PrintCenteredLine(message.ToUpper());

            Console.WriteLine("|                                                    |");

            // Assinatura final
            PrintCenteredLine("DTU");

            Console.WriteLine("|----------------------------------------------------|");
            Console.ResetColor();
        }

        // Função auxiliar para centralizar texto dentro da borda
        private void PrintCenteredLine(string text)
        {
            int totalWidth = 52;
            int padding = (totalWidth - text.Length) / 2;
            string line = "|" + new string(' ', padding) + text + new string(' ', totalWidth - text.Length - padding) + "|";
            Console.WriteLine(line);
        }
    }
}