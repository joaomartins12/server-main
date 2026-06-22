using DigitalWorldOnline.Commons.DTOs.Config;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UpdateGlobalDropsConfigCommand : IRequest
    {
        public GlobalDropsConfigDTO GlobalDrops { get; }

        public UpdateGlobalDropsConfigCommand(GlobalDropsConfigDTO globalDrops)
        {
            GlobalDrops = globalDrops;
        }
    }
}