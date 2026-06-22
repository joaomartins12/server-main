using DigitalWorldOnline.Commons.DTOs.Config;
using DigitalWorldOnline.Commons.Repositories.Admin;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UpdateAccessCommandHandler : IRequestHandler<UpdateAccessCommand>
    {
        private readonly IAdminCommandsRepository _repository;

        public UpdateAccessCommandHandler(IAdminCommandsRepository repository)
        {
            _repository = repository;
        }

        public async Task<Unit> Handle(UpdateAccessCommand request, CancellationToken cancellationToken)
        {
            var dto = new UserDTO()
            {
                Id = request.Id,
                AccessLevel = request.AccessLevel
            };

            await _repository.UpdateAccessAsync(dto);

            return Unit.Value;
        }
    }
}