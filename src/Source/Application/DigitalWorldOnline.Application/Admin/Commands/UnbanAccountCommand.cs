using MediatR;
using DigitalWorldOnline.Commons.Enums.Account;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UnbanAccountCommand : IRequest
    {
        public long AccountId { get; }

        public UnbanAccountCommand(long accountId)
        {
            AccountId = accountId;
        }
    }
}