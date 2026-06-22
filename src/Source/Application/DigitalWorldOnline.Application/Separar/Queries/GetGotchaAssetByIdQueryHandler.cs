using DigitalWorldOnline.Commons.DTOs.Assets;
using DigitalWorldOnline.Commons.Interfaces;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Queries
{
    public class GetGotchaAssetByIdQueryHandler : IRequestHandler<GetGotchaAssetByIdQuery, GotchaAssetDTO>
    {
        private readonly IServerQueriesRepository _repository;

        public GetGotchaAssetByIdQueryHandler(IServerQueriesRepository repository)
        {
            _repository = repository;
        }

        public async Task<GotchaAssetDTO> Handle(GetGotchaAssetByIdQuery request, CancellationToken cancellationToken)
        {
            return await _repository.GetGotchaAssetByIdAsync(request.MachineId);
        }
    }
}