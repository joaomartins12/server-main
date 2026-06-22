using DigitalWorldOnline.Commons.Enums.Account;

namespace DigitalWorldOnline.Commons.ViewModel.Account
{
    public class AccountUpdateViewModel
    {
        /// <summary>
        /// Unique sequential identifier.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Account username.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Account e-mail.
        /// </summary>
        public string Email { get; set; }

        /// <summary>
        /// Account access level.
        /// </summary>
        public AccountAccessLevelEnum AccessLevel { get; set; }

        /// <summary>
        /// Account premium coins.
        /// </summary>
        public int Premium { get; set; }

        /// <summary>
        /// Account silk(bônus) coins.
        /// </summary>
        public int Silk { get; set; }

        /// <summary>
        /// Discord ID do usuário.
        /// </summary>
        public string? DiscordId { get; set; }

        /// <summary>
        /// Account password.
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// Flag for empty fields.
        /// </summary>
        public bool Empty => string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Email);

        public AccountUpdateViewModel() { }

        public AccountBlockEnum? BlockType { get; set; }
        public string BlockReason { get; set; }
        public DateTime? BlockStartDate { get; set; }
        public DateTime? BlockEndDate { get; set; }

        public bool IsBanned => BlockType.HasValue && BlockStartDate.HasValue && (BlockEndDate == null || BlockEndDate > DateTime.Now);

        public AccountUpdateViewModel(
       long id,
       string username,
       string email,
       AccountAccessLevelEnum accessLevel,
       int premium,
       int silk,
       string discordId,
       //  string? password,
       AccountBlockEnum? blockType,
       string blockReason,
       DateTime? blockStartDate,
       DateTime? blockEndDate)
        {
            Id = id;
            Username = username;
            Email = email;
            AccessLevel = accessLevel;
            Premium = premium;
            Silk = silk;
            DiscordId = discordId;
            //  Password = password;
            BlockType = blockType;
            BlockReason = blockReason;
            BlockStartDate = blockStartDate;
            BlockEndDate = blockEndDate;
        }
    }
}