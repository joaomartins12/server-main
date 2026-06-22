using DigitalWorldOnline.Commons.DTOs.Character;
using DigitalWorldOnline.Commons.Enums;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class DeletePlayerActiveQuestsCommand : IRequest<bool>
    {
        public long CharacterId { get; }

        public DeletePlayerActiveQuestsCommand(long characterId)
        {
            CharacterId = characterId;
        }
    }
}
