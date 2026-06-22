using MediatR;
using DigitalWorldOnline.Application.Admin.Repositories;

namespace DigitalWorldOnline.Application.Admin.Queries
{
    public class GetGlobalDropsConfigsQueryHandler : IRequestHandler<GetGlobalDropsConfigsQuery, GetGlobalDropsConfigsQueryDto>
    {
        private readonly IAdminQueriesRepository _repository;

        public GetGlobalDropsConfigsQueryHandler(IAdminQueriesRepository repository)
        {
            _repository = repository;
        }

        public async Task<GetGlobalDropsConfigsQueryDto> Handle(GetGlobalDropsConfigsQuery request, CancellationToken cancellationToken)
        {
            return await _repository.GetGlobalDropsConfigsAsync(request.Limit, request.Offset, request.SortColumn, request.SortDirection, request.Filter);
        }
    }
}