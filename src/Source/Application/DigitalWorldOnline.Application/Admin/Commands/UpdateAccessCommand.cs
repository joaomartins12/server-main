using DigitalWorldOnline.Commons.Enums;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UpdateAccessCommand : IRequest
    {
        public long Id { get; set; }
        public UserAccessLevelEnum AccessLevel { get; set; }

        public UpdateAccessCommand(
            long id,
            UserAccessLevelEnum accessLevel)
        {
            Id = id;
            AccessLevel = accessLevel;
        }
    }
}