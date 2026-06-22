using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class DeleteGlobalDropsConfigCommandHandler : IRequestHandler<DeleteGlobalDropsConfigCommand>
    {
        private readonly IAdminCommandsRepository _repository;

        public DeleteGlobalDropsConfigCommandHandler(IAdminCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<Unit> Handle(DeleteGlobalDropsConfigCommand request, CancellationToken cancellationToken)
        {
            await _repository.DeleteGlobalDropsConfigAsync(request.Id);

            return Unit.Value;
        }
    }
}