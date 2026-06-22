using System.Collections.Generic;
using System.Linq;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Models.Character;

namespace DigitalWorldOnline.GameHost.Services
{
 /// <summary>
 /// Collects inventory mutations (add/remove/change bits) during a packet processing unit
 /// and flushes them in a single persistence operation.
 /// </summary>
 public sealed class InventoryMutationContext
 {
 private readonly ItemListModel _targetInventory;
 private readonly List<ItemModel> _pendingItems = new();
 private bool _bitsChanged;
 private long _bitsValue;

 public InventoryMutationContext(ItemListModel inventory)
 {
 _targetInventory = inventory;
 _bitsValue = inventory.Bits;
 }

 public void AddItems(IEnumerable<ItemModel> items)
 {
 foreach (var item in items)
 {
 _pendingItems.Add(item);
 }
 }

 public void RemoveItems(IEnumerable<ItemModel> items)
 {
 foreach (var item in items)
 {
 // naive remove by matching id+slot+amount; real logic can be improved
 var existing = _targetInventory.Items.FirstOrDefault(x => x.Id == item.Id);
 if (existing != null)
 {
 existing.Amount -= item.Amount;
 if (existing.Amount <=0)
 existing.Amount =0;
 }
 }
 }

 public void AddBits(long bits)
 {
 _bitsValue += bits;
 _bitsChanged = true;
 }

 public void RemoveBits(long bits)
 {
 _bitsValue -= bits;
 if (_bitsValue <0) _bitsValue =0;
 _bitsChanged = true;
 }

 public (List<ItemModel> Items, bool BitsChanged, long Bits) BuildFlush(bool includeExistingInventory = true)
 {
 var aggregate = new List<ItemModel>();

 if (includeExistingInventory)
 {
 // include current state
 aggregate.AddRange(_targetInventory.Items);
 }

 // include pending new items
 aggregate.AddRange(_pendingItems);

 // merge duplicates by Id+Slot
 var merged = aggregate
 .GroupBy(i => new { i.Id, i.Slot })
 .Select(g => {
 var first = g.First();
 first.Amount = g.Sum(x => x.Amount);
 return first;
 })
 .ToList();

 return (merged, _bitsChanged, _bitsValue);
 }
 }
}
