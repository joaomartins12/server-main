using DigitalWorldOnline.Commons.DTOs.Account;
using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UnbanAccountCommandHandler : IRequestHandler<UnbanAccountCommand>
    {
        private readonly IAdminCommandsRepository _repository;

        public UnbanAccountCommandHandler(IAdminCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<Unit> Handle(UnbanAccountCommand request, CancellationToken cancellationToken)
        {
            await _repository.RemoveAccountBlockAsync(request.AccountId);
            return Unit.Value;
        }
    }
}
