using DigitalWorldOnline.Commons.Models.Base;
using MediatR;

namespace DigitalWorldOnline.Application.Separar.Commands.Update
{
    public class UpdateItemsCommand : IRequest
    {
        public List<ItemModel> Items { get; }

        public ItemModel Item { get; }

        /// <summary>
        /// When true, handler should persist directly (write-through) rather than enqueueing to background write-behind.
        /// </summary>
        public bool ForceSync { get; }

        public UpdateItemsCommand(List<ItemModel> items, bool forceSync = false)
        {
            Items = items;
            ForceSync = forceSync;
        }

        public UpdateItemsCommand(ItemListModel itemList, bool forceSync = false)
        {
            Items = itemList.Items;
            ForceSync = forceSync;
        }

        public UpdateItemsCommand(ItemModel item, bool forceSync = false)
        {
            Item = item;
            ForceSync = forceSync;
        }
    }
}