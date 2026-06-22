using MediatR;

namespace DigitalWorldOnline.Application.Admin.Queries
{
    public class GetGlobalDropsConfigByIdQuery : IRequest<GetGlobalDropsConfigByIdQueryDto>
    {
        public long Id { get; }

        public GetGlobalDropsConfigByIdQuery(long id)
        {
            Id = id;
        }
    }
}