using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UpdateGotchaAssetCommandHandler : IRequestHandler<UpdateGotchaAssetCommand, bool>
    {
        private readonly IAdminCommandsRepository _repository;

        public UpdateGotchaAssetCommandHandler(IAdminCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<bool> Handle(UpdateGotchaAssetCommand request, CancellationToken cancellationToken)
        {
            return await _repository.UpdateGotchaAssetAsync(request.Machine);
        }
    }
}