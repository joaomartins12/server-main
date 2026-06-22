using DigitalWorldOnline.Commons.DTOs.Config;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class CreateGlobalDropsConfigCommand : IRequest<GlobalDropsConfigDTO>
    {
        public GlobalDropsConfigDTO GlobalDrops { get; }

        public CreateGlobalDropsConfigCommand(GlobalDropsConfigDTO globalDrops)
        {
            GlobalDrops = globalDrops;
        }
    }
}