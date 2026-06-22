using MediatR;
using DigitalWorldOnline.Commons.DTOs.Assets;

namespace DigitalWorldOnline.Application.Separar.Queries
{
    public class GetGotchaAssetByIdQuery : IRequest<GotchaAssetDTO>
    {
        public int MachineId { get; }

        public GetGotchaAssetByIdQuery(int machineId)
        {
            MachineId = machineId;
        }
    }
}