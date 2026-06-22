using DigitalWorldOnline.Commons.DTOs.Character;
using DigitalWorldOnline.Commons.Enums;
using MediatR;

namespace DigitalWorldOnline.Application.Admin.Commands
{
    public class UpdatePlayerQuestsCommand : IRequest<bool>
    {
        public long CharacterId { get; }
        public short QuestId { get; }
        public bool IsCompleted { get; }
        public byte[] CompletedData { get; }
        public int[] CompletedDataValue { get; }

        public UpdatePlayerQuestsCommand(
            long characterId,
            short questId,
            bool isCompleted,
            byte[] completedData = null,
            int[] completedDataValue = null)
        {
            CharacterId = characterId;
            QuestId = questId;
            IsCompleted = isCompleted;
            CompletedData = completedData ?? new byte[768];
            CompletedDataValue = completedDataValue ?? new int[192];
        }
    }
}