using AutoMapper;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Delete;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Account;
using DigitalWorldOnline.Commons.Models.Asset;
using DigitalWorldOnline.Commons.Models.Character;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.CharacterServer;
using DigitalWorldOnline.Commons.Utils;
using MediatR;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Security.Cryptography;
using System.Text;

namespace DigitalWorldOnline.Character
{
    public sealed class CharacterPacketProcessor : IProcessor, IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly ISender _sender;
        private readonly ILogger _logger;
        private readonly IMapper _mapper;

        private const string GameServerAddress = "GameServer:Address";
        private const string GamerServerPublic = "GameServer:PublicAddress";
        private const string GameServerPort = "GameServer:Port";

        private const int HandshakeDegree = 32321;
        private const int HandshakeStampDegree = 65535;

        public CharacterPacketProcessor(
            ILogger logger,
            ISender sender,
            IConfiguration configuration,
            IMapper mapper)
        {
            _configuration = configuration;
            _sender = sender;
            _logger = logger;
            _mapper = mapper;
        }

        public async Task ProcessPacketAsync(GameClient client, byte[] data)
        {
            var packet = new CharacterPacketReader(data);

            switch (packet.Enum)
            {
                case CharacterServerPacketEnum.Connection:
                    {
                        _logger.Debug("Reading packet parameters...");

                        var kind = packet.ReadByte();

                        var handshakeTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var handshake = (short)(client.Handshake ^ HandshakeDegree);

                        client.Send(new ConnectionPacket(handshake, handshakeTimestamp).Serialize());
                    }
                    break;

                case CharacterServerPacketEnum.KeepConnection:
                    break;

                case CharacterServerPacketEnum.RequestCharacters:
                    {
                        packet.Seek(8);

                        _logger.Debug("Requesting Characters");

                        var accountId = packet.ReadUInt();

                        var characters = _mapper.Map<List<CharacterModel>>(
                            await _sender.Send(new CharactersByAccountIdQuery(accountId)));

                        characters.ForEach(character =>
                        {
                            if (character.Partner.CurrentType != character.Partner.BaseType)
                            {
                                _logger.Debug("Updating partner's current type...");

                                character.Partner.UpdateCurrentType(character.Partner.BaseType);

                                _sender.Send(new UpdatePartnerCurrentTypeCommand(character.Partner));
                            }
                        });

                        client.Send(new CharacterListPacket(characters));

                        client.SetAccountId(accountId);
                    }
                    break;

                case CharacterServerPacketEnum.CreateCharacter:
                    {
                        _logger.Debug("Reading packet parameters...");

                        var position = packet.ReadByte();

                        var tamerModel = packet.ReadInt();

                        var tamerName = packet.ReadZString();

                        packet.Seek(42);

                        var digimonModel = packet.ReadInt();

                        var digimonName = packet.ReadZString();

                        tamerName = CleanCreateName(tamerName);
                        digimonName = CleanCreateName(digimonName);

                        if (!IsValidCreateName(tamerName))
                        {
                            _logger.Warning(
                                "Invalid tamer name on character creation. AccountId: {AccountId}, Name: {Name}, Length: {Length}",
                                client.AccountId,
                                tamerName,
                                tamerName.Length);

                            client.Send(new AvailableNamePacket(false).Serialize());
                            break;
                        }

                        if (!IsValidCreateName(digimonName))
                        {
                            _logger.Warning(
                                "Invalid digimon name on character creation. AccountId: {AccountId}, Name: {Name}, Length: {Length}",
                                client.AccountId,
                                digimonName,
                                digimonName.Length);

                            client.Send(new AvailableNamePacket(false).Serialize());
                            break;
                        }

                        List<int> allowedDigimonModels = new List<int>
                    {
                        31001,
                        31002,
                        31003,
                        31004
                    };

                        List<int> allowedTamerModels = new List<int>
                    {
                        80001,
                        80002,
                        80003,
                        80004
                    };

                        if (!allowedDigimonModels.Contains(digimonModel))
                        {
                            _logger.Warning($"Player {client.AccountId} tryed to make a digimon that is not oppened on pack03");

                            client.SetGameQuit(true);
                            client.Disconnect();

                            return;
                        }

                        if (!allowedTamerModels.Contains(tamerModel))
                        {
                            _logger.Warning($"Player {client.AccountId} tryed to make a character that is not oppened on pack03");

                            client.SetGameQuit(true);
                            client.Disconnect();

                            return;
                        }

                        _logger.Debug($"Searching account with id {client.AccountId}...");

                        var account = _mapper.Map<AccountModel>(
                            await _sender.Send(new AccountByIdQuery(client.AccountId)));

                        if (account == null)
                        {
                            _logger.Warning(
                                "Account not found on character creation. AccountId: {AccountId}",
                                client.AccountId);

                            client.Send(new AvailableNamePacket(false).Serialize());
                            break;
                        }

                        /*
                         * Proteção final contra nomes duplicados.
                         * Mesmo que o client avance por algum erro, o server não deixa criar
                         * um Tamer com nome já existente.
                         */
                        var duplicatedCharacter = await _sender.Send(new CharacterByNameQuery(tamerName));

                        if (duplicatedCharacter != null)
                        {
                            _logger.Warning(
                                "[Character Creation] Tamer name already exists on final create. AccountId: {AccountId}, Name: {Name}",
                                client.AccountId,
                                tamerName);

                            client.Send(new AvailableNamePacket(false).Serialize());
                            break;
                        }

                        //tamerName = tamerName.ModeratorPrefix(account.AccessLevel);

                        _logger.Debug("Creating character...");

                        var character = CharacterModel.Create(
                            client.AccountId,
                            tamerName,
                            tamerModel,
                            position,
                            account.LastPlayedServer);

                        _logger.Debug("Creating digimon...");

                        var digimon = DigimonModel.Create(
                            digimonName,
                            digimonModel,
                            digimonModel,
                            DigimonHatchGradeEnum.Perfect,
                            UtilitiesFunctions.RandomShort(12000, 12000),
                            0);

                        character.AddDigimon(digimon);

                        var handshakeTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var handshake = (short)(handshakeTimestamp & HandshakeStampDegree);

                        client.Send(new CharacterCreatedPacket(character, handshake));

                        _logger.Debug("Getting tamer status information...");

                        character.SetBaseStatus(
                            _mapper.Map<CharacterBaseStatusAssetModel>(
                                await _sender.Send(
                                    new TamerBaseStatusQuery(character.Model))));

                        character.SetLevelStatus(
                            _mapper.Map<CharacterLevelStatusAssetModel>(
                                await _sender.Send(
                                    new TamerLevelStatusQuery(
                                        character.Model,
                                        character.Level))));

                        character.Partner.SetBaseInfo(
                            _mapper.Map<DigimonBaseInfoAssetModel>(
                                await _sender.Send(
                                    new DigimonBaseInfoQuery(character.Partner.CurrentType))));

                        _logger.Debug($"Registering tamer and digimon for account {account.Username}...");

                        character.Partner.AddEvolutions(
                            await _sender.Send(new DigimonEvolutionAssetsByTypeQuery(digimonModel)));

                        await _sender.Send(new CreateCharacterCommand(character));
                    }
                    break;

                case CharacterServerPacketEnum.CheckNameDuplicity:
                    {
                        _logger.Debug("Getting parameters...");

                        var tamerName = packet.ReadString();

                        tamerName = CleanCreateName(tamerName);

                        if (!IsValidCreateName(tamerName))
                        {
                            _logger.Warning(
                                "Invalid tamer name on name duplicity check. AccountId: {AccountId}, Name: {Name}, Length: {Length}",
                                client.AccountId,
                                tamerName,
                                tamerName.Length);

                            client.Send(new AvailableNamePacket(false).Serialize());
                            break;
                        }

                        _logger.Debug($"Account: {client.AccountId} - {tamerName}");
                        _logger.Debug("Searching account...");

                        var account = _mapper.Map<AccountModel>(
                            await _sender.Send(new AccountByIdQuery(client.AccountId)));

                        if (account == null)
                        {
                            client.Send(new AvailableNamePacket(false).Serialize());
                            break;
                        }

                        /*
                         * IMPORTANTE:
                         * A criação do personagem está a usar o nome raw/limpo:
                         *
                         * CharacterModel.Create(..., tamerName, ...)
                         *
                         * Por isso a verificação de duplicado também tem de procurar pelo mesmo nome.
                         * Não usamos ModeratorPrefix aqui porque no CreateCharacter também está comentado.
                         */
                        var existingCharacter = await _sender.Send(new CharacterByNameQuery(tamerName));

                        var availableName = existingCharacter == null;

                        _logger.Information(
                            "[Character Creation] Name duplicity check. AccountId: {AccountId}, Name: {Name}, Available: {Available}",
                            client.AccountId,
                            tamerName,
                            availableName);

                        client.Send(new AvailableNamePacket(availableName).Serialize());
                    }
                    break;

                case CharacterServerPacketEnum.DeleteCharacter:
                    {
                        _logger.Information($"Reading delete character packet parameters...");

                        var position = packet.ReadByte();

                        // O client envia 1 byte de position + 3 bytes padding antes da string.
                        packet.Skip(3);

                        var validation = packet.ReadString();

                        _logger.Information($"Searching account with id {client.AccountId}...");

                        var account = _mapper.Map<AccountModel>(
                            await _sender.Send(new AccountByIdQuery(client.AccountId))
                        );

                        _logger.Warning(
                            "[Character Deletion] Validation debug. Account={Username} Position={Position} ReceivedLen={ReceivedLen} StoredSecondLen={StoredSecondLen} EmailLen={EmailLen}",
                            account.Username,
                            position,
                            validation?.Length ?? 0,
                            account.SecondaryPassword?.Length ?? 0,
                            account.Email?.Length ?? 0
                        );

                        if (IsValidCharacterDeleteValidation(account, validation))
                        {
                            _logger.Information($"Fetching character details for deletion...");

                            var character = _mapper.Map<CharacterModel>(
                                await _sender.Send(new CharacterByAccountIdAndPositionQuery(client.AccountId, position))
                            );

                            if (character != null)
                            {
                                _logger.Information(
                                    $"[Character Deletion] AccountId: {client.AccountId}, Position: {position}, Character Name: {character.Name} - Deleting character..."
                                );

                                var deletedCharacter = await _sender.Send(new DeleteCharacterCommand(client.AccountId, position));

                                client.Send(new CharacterDeletedPacket(deletedCharacter).Serialize());

                                _logger.Information(
                                    $"[Character Deletion] Character '{character.Name}' (Position: {position}) successfully deleted from AccountId: {client.AccountId}."
                                );
                            }
                            else
                            {
                                _logger.Warning(
                                    $"[Character Deletion] AccountId: {client.AccountId}, Position: {position} - Character not found!"
                                );

                                client.Send(new CharacterDeletedPacket(DeleteCharacterResultEnum.ValidationFail).Serialize());
                            }
                        }
                        else
                        {
                            _logger.Warning(
                                "[Character Deletion] Validation failed. Account={Username}, Position={Position}, ReceivedLen={ReceivedLen}, StoredSecondLen={StoredSecondLen}",
                                account.Username,
                                position,
                                validation?.Length ?? 0,
                                account.SecondaryPassword?.Length ?? 0
                            );

                            client.Send(new CharacterDeletedPacket(DeleteCharacterResultEnum.ValidationFail).Serialize());
                        }
                    }
                    break;

                case CharacterServerPacketEnum.GetCharacterPosition:
                    {
                        var position = packet.ReadByte();

                        _logger.Debug("Searching character...");

                        var character = _mapper.Map<CharacterModel>(
                            await _sender.Send(new CharacterByAccountIdAndPositionQuery(client.AccountId, position)));

                        while (character == null)
                        {
                            await Task.Delay(1500);

                            _logger.Debug("Searching character again...");

                            character = _mapper.Map<CharacterModel>(
                                await _sender.Send(new CharacterByAccountIdAndPositionQuery(client.AccountId, position)));
                        }

                        _logger.Debug($"Updating access information for account {client.AccountId}.");

                        await _sender.Send(new UpdateLastPlayedCharacterCommand(client.AccountId, character.Id));

                        _logger.Debug("Updating character's channel...");

                        await _sender.Send(new UpdateCharacterChannelCommand(character.Id));

                        _logger.Debug("Updating account welcome flag...");

                        await _sender.Send(new UpdateAccountWelcomeFlagCommand(character.AccountId));

                        _logger.Debug("Updating character send once packet...");

                        await _sender.Send(new UpdateCharacterInitialPacketSentOnceSentCommand(character.Id, false));

                        if (UtilitiesFunctions.DungeonMapIds.Contains(character.Location.MapId))
                        {
                            character.NewLocation(3, 19907, 15514);

                            await _sender.Send(new UpdateCharacterLocationCommand(character.Location));
                        }

                        _logger.Debug("Sending selected server info...");

                        client.Send(new ConnectGameServerInfoPacket(
                            _configuration[GameServerAddress],
                            _configuration[GameServerPort],
                            character.Location.MapId).Serialize());
                    }
                    break;

                case CharacterServerPacketEnum.ConnectGameServer:
                    {
                        _logger.Debug("Sending answer to connect to game server...");

                        client.Send(new ConnectGameServerPacket().Serialize());
                    }
                    break;

                default:
                    _logger.Warning($"Unknown packet. Type: {packet.Type} Length: {packet.Length}.");
                    break;
            }
        }

        private static bool IsCharacterDeleteValidationValid(AccountModel account, string validation)
        {
            if (account == null)
                return false;

            if (string.IsNullOrWhiteSpace(validation))
                return false;

            var cleanValidation = validation.Trim();

            if (!string.IsNullOrWhiteSpace(account.Email) &&
                string.Equals(account.Email.Trim(), cleanValidation, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(account.SecondaryPassword))
                return false;

            var storedSecondPassword = NormalizeSecondPasswordHash(account.SecondaryPassword);
            var receivedSecondPassword = NormalizeSecondPasswordHash(cleanValidation);

            if (string.IsNullOrWhiteSpace(storedSecondPassword) ||
                string.IsNullOrWhiteSpace(receivedSecondPassword))
            {
                return false;
            }

            return string.Equals(storedSecondPassword, receivedSecondPassword, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidCharacterDeleteValidation(AccountModel account, string? receivedValidation)
        {
            if (account == null || string.IsNullOrWhiteSpace(receivedValidation))
                return false;

            var received = receivedValidation.Trim();

            // Compatibilidade antiga: aceitar email.
            if (!string.IsNullOrWhiteSpace(account.Email))
            {
                var email = account.Email.Trim();

                if (SecureEquals(received, email))
                    return true;
            }

            if (string.IsNullOrWhiteSpace(account.SecondaryPassword))
                return false;

            var stored = account.SecondaryPassword.Trim();

            /*
                Formatos aceites:

                - plain:
                    631998

                - Base64 plain, usado pelo teu Extensions.Encrypt():
                    NjMxOTk4

                - MD5 32:
                    md5 completo em hex

                - MD5 16:
                    alguns clients antigos usam 16 caracteres do MD5.
                    Para máxima compatibilidade aceitamos:
                    - primeiros 16
                    - 16 do meio
                    - últimos 16
            */

            var storedVariants = BuildPasswordVariants(stored);
            var receivedVariants = BuildPasswordVariants(received);

            foreach (var storedVariant in storedVariants)
            {
                foreach (var receivedVariant in receivedVariants)
                {
                    if (SecureEquals(storedVariant, receivedVariant))
                        return true;
                }
            }

            return false;
        }

        private static HashSet<string> BuildPasswordVariants(string value)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(value))
                return result;

            value = value.Trim();

            AddVariant(result, value);

            var base64Decoded = TryBase64Decode(value);

            if (!string.IsNullOrWhiteSpace(base64Decoded))
                AddVariant(result, base64Decoded);

            foreach (var current in result.ToList())
            {
                var md5 = Md5Hex32(current);

                AddVariant(result, md5);

                if (md5.Length == 32)
                {
                    AddVariant(result, md5.Substring(0, 16));
                    AddVariant(result, md5.Substring(8, 16));
                    AddVariant(result, md5.Substring(16, 16));
                }
            }

            return result;
        }

        private static void AddVariant(HashSet<string> variants, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            variants.Add(value.Trim());
        }

        private static string Md5Hex32(string input)
        {
            using var md5 = MD5.Create();

            var bytes = md5.ComputeHash(Encoding.ASCII.GetBytes(input));

            var sb = new StringBuilder(bytes.Length * 2);

            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }

        private static string? TryBase64Decode(string input)
        {
            try
            {
                var bytes = Convert.FromBase64String(input);
                return Encoding.ASCII.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        private static bool SecureEquals(string? a, string? b)
        {
            if (a == null || b == null)
                return false;

            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSecondPasswordHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var cleaned = value.Trim();

            if (cleaned.Length != 32)
                return cleaned.ToLowerInvariant();

            foreach (var character in cleaned)
            {
                var isNumber = character >= '0' && character <= '9';
                var isLowerHex = character >= 'a' && character <= 'f';
                var isUpperHex = character >= 'A' && character <= 'F';

                if (!isNumber && !isLowerHex && !isUpperHex)
                    return cleaned.ToLowerInvariant();
            }

            return cleaned.ToLowerInvariant();
        }

        private static string CleanCreateName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            var cleanName = name;

            var nullIndex = cleanName.IndexOf('\0');

            if (nullIndex >= 0)
                cleanName = cleanName.Substring(0, nullIndex);

            return cleanName.Trim();
        }

        private static bool IsValidCreateName(string name)
        {
            name = CleanCreateName(name);

            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (name.Length < 2 || name.Length > 16)
                return false;

            foreach (var character in name)
            {
                var isNumber = character >= '0' && character <= '9';
                var isUpper = character >= 'A' && character <= 'Z';
                var isLower = character >= 'a' && character <= 'z';

                if (!isNumber && !isUpper && !isLower)
                    return false;
            }

            return true;
        }

        private void DebugLog(string message)
        {
            _logger?.Debug($"{message}");
        }

        private void InfoLog(string message)
        {
            _logger?.Information($"{message}");
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}