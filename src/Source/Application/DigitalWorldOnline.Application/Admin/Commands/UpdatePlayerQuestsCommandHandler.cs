using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Handlers
{
    public class UpdatePlayerQuestsCommandHandler : IRequestHandler<UpdatePlayerQuestsCommand, bool>
    {
        private readonly IAdminCommandsRepository _repository;

        public UpdatePlayerQuestsCommandHandler(IAdminCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<bool> Handle(UpdatePlayerQuestsCommand request, CancellationToken cancellationToken)
        {
            return await _repository.UpdatePlayerQuestsAsync(
                request.CharacterId,
                request.QuestId,
                request.IsCompleted
            );
        }
    }
}