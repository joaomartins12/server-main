using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Application.Admin.Handlers
{
    public class DeletePlayerActiveQuestsCommandHandler : IRequestHandler<DeletePlayerActiveQuestsCommand, bool>
    {
        private readonly IAdminCommandsRepository _repository;

        public DeletePlayerActiveQuestsCommandHandler(IAdminCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<bool> Handle(DeletePlayerActiveQuestsCommand request, CancellationToken cancellationToken)
        {
            return await _repository.DeletePlayerActiveQuestsAsync(request.CharacterId);
        }
    }
}
