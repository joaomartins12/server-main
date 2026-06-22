using MediatR;
using DigitalWorldOnline.Application.Admin.Repositories;

namespace DigitalWorldOnline.Application.Admin.Queries
{
    public class GetGlobalDropsConfigByIdQueryHandler : IRequestHandler<GetGlobalDropsConfigByIdQuery, GetGlobalDropsConfigByIdQueryDto>
    {
        private readonly IAdminQueriesRepository _repository;

        public GetGlobalDropsConfigByIdQueryHandler(IAdminQueriesRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetGlobalDropsConfigByIdQueryDto> Handle(GetGlobalDropsConfigByIdQuery request, CancellationToken cancellationToken)
        {
            return await _repository.GetGlobalDropsConfigByIdAsync(request.Id);
        }
    }
}