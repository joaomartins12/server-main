using MediatR;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using DigitalWorldOnline.Commons.ViewModel.Players;
using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Commons.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DigitalWorldOnline.Application;
using static MudBlazor.CategoryTypes;
using DigitalWorldOnline.Commons.Models.Map;
using AutoMapper;
using DigitalWorldOnline.Commons.ViewModel.Asset;

namespace DigitalWorldOnline.Admin.Pages.Players
{
    public partial class PlayerInventory
    {

        [Parameter] public long PlayerId { get; set; }

        [Inject]
        public ISender Sender { get; set; }

        [Inject]
        public IMapper Mapper { get; set; }

        [Inject]
        public ISnackbar Toast { get; set; }

        [Inject] public ISnackbar Snackbar { get; set; }
        [Inject] public IDialogService DialogService { get; set; }

        private string? PlayerName;
        private bool Loading = true;
        
        private List<InventoryItemViewModel> MainInventoryItems = new();
        private List<InventoryItemViewModel> WarehouseItems = new();
        private List<InventoryItemViewModel> EquippedItems = new();

        protected override async Task OnInitializedAsync()
        {
            await LoadInventoryData();
        }

        private async Task LoadInventoryData()
        {
            try
            {
                Loading = true;
                
                // Get player inventory from database
                var inventoryResult = await Sender.Send(new GetPlayerInventoryQuery(PlayerId));

                if (inventoryResult?.Player != null)
                {
                    PlayerName = inventoryResult.Player.Name;

                    // Get different inventory types
                    var mainInventory = inventoryResult.Player.ItemList?.FirstOrDefault(x => x.Type == ItemListEnum.Inventory);
                    var warehouse = inventoryResult.Player.ItemList?.FirstOrDefault(x => x.Type == ItemListEnum.Warehouse);
                    var equipment = inventoryResult.Player.ItemList?.FirstOrDefault(x => x.Type == ItemListEnum.Equipment);

                    // Convert to ViewModel
                    MainInventoryItems = await ConvertToInventoryItemsAsync(mainInventory?.Items);
                    WarehouseItems = await ConvertToInventoryItemsAsync(warehouse?.Items);
                    EquippedItems = await ConvertToInventoryItemsAsync(equipment?.Items);
                }
                else
                {
                    Snackbar.Add("Player not found", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error loading inventory: {ex.Message}", Severity.Error);
            }
            finally
            {
                Loading = false;
            }
        }

        private async Task<List<InventoryItemViewModel>> ConvertToInventoryItemsAsync(List<DigitalWorldOnline.Commons.DTOs.Base.ItemDTO>? items)
        {
            if (items == null) return new List<InventoryItemViewModel>();

            var tasks = items
                .Where(x => x.ItemId > 0)
                .Select(async item =>
                {
                    var itemInfoQuery = await Sender.Send(new GetItemAssetByIdQuery(item.ItemId));
                    var itemInfo = Mapper.Map<ItemAssetViewModel>(itemInfoQuery.Register);

                    return new InventoryItemViewModel
                    {
                        ItemId = item.ItemId,
                        ItemName = itemInfo?.Name ?? "Unknown",
                        Slot = item.Slot,
                        Amount = item.Amount
                    };
                });

            var result = await Task.WhenAll(tasks);
            return result.ToList();
        }

        private async Task EditItem(InventoryItemViewModel item)
        {
            var parameters = new DialogParameters
            {
                ["Item"] = item,
                ["PlayerId"] = PlayerId
            };

            // TODO: Implement EditItemDialog
            Snackbar.Add("Edit item functionality not implemented yet", Severity.Info);
        }

        private async Task DeleteItem(InventoryItemViewModel item)
        {
            bool? result = await DialogService.ShowMessageBox(
                "Delete Item",
                $"Are you sure you want to delete '{item.ItemName}'?",
                yesText: "Delete", cancelText: "Cancel");

            if (result == true)
            {
                try
                {
                    // TODO: Implement delete item command
                    // await Sender.Send(new DeleteInventoryItemCommand(PlayerId, item.Slot, item.ItemId));
                    
                    await LoadInventoryData(); // Refresh data
                    Snackbar.Add("Item deleted successfully!", Severity.Success);
                }
                catch (Exception ex)
                {
                    Snackbar.Add($"Error deleting item: {ex.Message}", Severity.Error);
                }
            }
        }
    }

    // ViewModel for inventory items - adjust based on your actual data structure
    public class InventoryItemViewModel
    {
        public long ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int Slot { get; set; }
        public int Amount { get; set; }
    }
}
