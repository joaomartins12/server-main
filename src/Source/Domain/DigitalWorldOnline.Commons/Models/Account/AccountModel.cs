using DigitalWorldOnline.Commons.Enums;
using DigitalWorldOnline.Commons.Enums.Account;
using DigitalWorldOnline.Commons.Extensions;
using DigitalWorldOnline.Commons.Models.Base;
using System.Security.Cryptography;
using System.Text;

namespace DigitalWorldOnline.Commons.Models.Account
{
    public class AccountModel
    {
        public long Id { get; set; }

        public string Username { get; set; }

        public string Password { get; set; }

        public string? SecondaryPassword { get; set; }

        public string Email { get; set; }

        public string DiscordId { get; set; }

        public AccountAccessLevelEnum AccessLevel { get; set; }

        public DateTime CreateDate { get; set; }

        public DateTime? LastConnection { get; set; }

        public DateTime? MembershipExpirationDate { get; set; }

        public int Premium { get; set; }

        public int Silk { get; set; }

        public long LastPlayedServer { get; set; }

        public long LastPlayedCharacter { get; set; }

        public SystemInformationModel? SystemInformation { get; set; }

        public AccountBlockModel? AccountBlock { get; set; }

        public List<ItemListModel> ItemList { get; private set; }

        public bool ReceiveWelcome { get; private set; }

        public static AccountModel Create(
            string username,
            string password,
            string email,
            string discordId,
            string? secondaryPassword = null,
            AccountAccessLevelEnum accessLevel = AccountAccessLevelEnum.Default,
            int premium = 0,
            int silk = 0,
            SystemInformationModel? systemInformation = null,
            AccountBlockModel? accountBlock = null)
        {
            return new AccountModel()
            {
                Username = username,
                Password = password,
                Email = email,
                SecondaryPassword = secondaryPassword,
                AccessLevel = accessLevel,
                CreateDate = DateTime.Now,
                Premium = premium,
                Silk = silk,
                LastPlayedServer = 0,
                SystemInformation = systemInformation,
                AccountBlock = accountBlock,
                DiscordId = discordId,
                MembershipExpirationDate = DateTime.Now,

                ItemList = new List<ItemListModel>()
                {
                    new ItemListModel(ItemListEnum.AccountWarehouse),
                    new ItemListModel(ItemListEnum.CashWarehouse),
                    new ItemListModel(ItemListEnum.ShopWarehouse),
                    new ItemListModel(ItemListEnum.BuyHistory)
                }
            };
        }

        public static AccountModel Create(
            string username,
            string email,
            string discordId,
            string password)
        {
            return new AccountModel()
            {
                Username = username,
                Password = password.Encrypt(),
                Email = email,
                AccessLevel = AccountAccessLevelEnum.Default,
                CreateDate = DateTime.Now,
                DiscordId = discordId,

                ItemList = new List<ItemListModel>()
                {
                    new ItemListModel(ItemListEnum.AccountWarehouse),
                    new ItemListModel(ItemListEnum.CashWarehouse),
                    new ItemListModel(ItemListEnum.ShopWarehouse),
                    new ItemListModel(ItemListEnum.BuyHistory)
                }
            };
        }

        public bool CharacterDeleteValidation(string validation)
        {
            if (string.IsNullOrWhiteSpace(validation))
                return false;

            validation = validation.Trim();

            // Compatibilidade antiga: alguns clients permitem apagar usando email.
            if (!string.IsNullOrWhiteSpace(Email) &&
                string.Equals(validation, Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Validação principal: secondary password plain / MD5 32 / MD5 16.
            return IsValidSecondPassword(SecondaryPassword, validation);
        }

        private static bool IsValidSecondPassword(string? storedPassword, string receivedPassword)
        {
            if (string.IsNullOrWhiteSpace(storedPassword) ||
                string.IsNullOrWhiteSpace(receivedPassword))
            {
                return false;
            }

            storedPassword = storedPassword.Trim();
            receivedPassword = receivedPassword.Trim();

            /*
                Casos suportados:

                1) DB plain, client plain
                   stored:   631998
                   received: 631998

                2) DB plain, client MD5 32
                   stored:   631998
                   received: md5_32(631998)

                3) DB plain, client MD5 16
                   stored:   631998
                   received: md5_16(631998)

                4) DB MD5 32, client plain
                   stored:   md5_32(631998)
                   received: 631998

                5) DB MD5 16, client plain
                   stored:   md5_16(631998)
                   received: 631998

                6) DB MD5 16/32, client MD5 16/32 igual
            */

            if (string.Equals(storedPassword, receivedPassword, StringComparison.OrdinalIgnoreCase))
                return true;

            var storedMd5_32 = Md5Hex32(storedPassword);
            var storedMd5_16 = Md5Hex16(storedPassword);

            var receivedMd5_32 = Md5Hex32(receivedPassword);
            var receivedMd5_16 = Md5Hex16(receivedPassword);

            // DB plain, client envia hash
            if (string.Equals(storedMd5_32, receivedPassword, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(storedMd5_16, receivedPassword, StringComparison.OrdinalIgnoreCase))
                return true;

            // DB hash, client envia plain
            if (string.Equals(storedPassword, receivedMd5_32, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(storedPassword, receivedMd5_16, StringComparison.OrdinalIgnoreCase))
                return true;

            // Compat extra: comparar hash contra hash calculado dos dois lados
            if (string.Equals(storedMd5_32, receivedMd5_32, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(storedMd5_16, receivedMd5_16, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
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

        private static string Md5Hex16(string input)
        {
            var md5 = Md5Hex32(input);

            // Formato antigo comum em clients DMO:
            // 16 caracteres centrais do MD5 de 32 caracteres.
            return md5.Substring(8, 16);
        }

        public void BlockUnblock()
        {
            if (AccessLevel == AccountAccessLevelEnum.Blocked)
                AccessLevel = AccountAccessLevelEnum.Default;
            else
                AccessLevel = AccountAccessLevelEnum.Blocked;
        }
    }
}