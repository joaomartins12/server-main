using DigitalWorldOnline.Commons.DTOs.Assets;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UpdateGotchaAssetCommand : IRequest<bool>
    {
        public GotchaAssetDTO Machine { get; }

        public UpdateGotchaAssetCommand(GotchaAssetDTO machine)
        {
            Machine = machine;
        }
    }
}
