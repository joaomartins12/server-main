using DigitalWorldOnline.Commons.Models.Base;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Commands.Update
{
    public class UpdateItemListSizeCommand : IRequest
    {
        public long ItemListId { get; }
        public short NewSize { get; }

        // Preferir este ctor (short)
        public UpdateItemListSizeCommand(long itemListId, short newSize)
        {
            ItemListId = itemListId;
            NewSize = newSize;
        }

        // Compatibilidade temporária: ainda aceita byte e converte para short
        [System.Obsolete("Use o construtor que recebe 'short'.")]
        public UpdateItemListSizeCommand(long itemListId, byte newSize)
        {
            ItemListId = itemListId;
            NewSize = newSize; // cast implícito byte->short, sem perda
        }

        // Construtor a partir do model (agora Size é short)
        public UpdateItemListSizeCommand(ItemListModel itemList)
        {
            ItemListId = itemList.Id;
            NewSize = itemList.Size;
        }
    }
}
