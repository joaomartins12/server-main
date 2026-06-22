using DigitalWorldOnline.Commons.DTOs.Config;
using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class CreateGlobalDropsConfigCommandHandler : IRequestHandler<CreateGlobalDropsConfigCommand, GlobalDropsConfigDTO>
    {
        private readonly IAdminCommandsRepository _repository;

        public CreateGlobalDropsConfigCommandHandler(IAdminCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<GlobalDropsConfigDTO> Handle(CreateGlobalDropsConfigCommand request, CancellationToken cancellationToken)
        {
            return await _repository.AddGlobalDropsConfigAsync(request.GlobalDrops);
        }
    }
}