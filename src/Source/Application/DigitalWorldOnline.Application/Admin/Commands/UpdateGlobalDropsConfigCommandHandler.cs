using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UpdateGlobalDropsConfigCommandHandler : IRequestHandler<UpdateGlobalDropsConfigCommand>
    {
        private readonly IAdminCommandsRepository _repository;

        public UpdateGlobalDropsConfigCommandHandler(IAdminCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<Unit> Handle(UpdateGlobalDropsConfigCommand request, CancellationToken cancellationToken)
        {
            await _repository.UpdateGlobalDropsConfigAsync(request.GlobalDrops);

            return Unit.Value;
        }
    }
}