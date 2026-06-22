using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class DeleteGlobalDropsConfigCommand : IRequest
    {
        public long Id { get; set; }

        public DeleteGlobalDropsConfigCommand(long id)
        {
            Id = id;
        }
    }
}