using DigitalWorldOnline.Commons.Models.Base;

namespace DigitalWorldOnline.Application.Separar.Commands.Update
{
 public interface IUpdateItemsBackgroundQueue
 {
 void Enqueue(List<ItemModel> items);

 Task StartProcessing(CancellationToken cancellationToken);
 }
}