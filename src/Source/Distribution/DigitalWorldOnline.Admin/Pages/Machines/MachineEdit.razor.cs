using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.DTOs.Assets;
using MediatR;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Admin.Pages.Machines
{
    public partial class MachineEdit : ComponentBase
    {
        [Parameter]
        public int MachineId { get; set; }

        [Inject]
        private ISender Sender { get; set; }

        [Inject]
        private ISnackbar Snackbar { get; set; }

        [Inject]
        private NavigationManager Navigation { get; set; }

        private GotchaAssetDTO? Machine { get; set; }
        private bool Loading { get; set; } = true;
        private bool Saving { get; set; } = false;

        protected override async Task OnInitializedAsync()
        {
            await LoadMachine();
        }

        private async Task LoadMachine()
        {
            try
            {
                Loading = true;
                var result = await Sender.Send(new GetGotchaAssetByIdQuery(MachineId));
                Machine = result ?? throw new Exception("Machine not found");

                if (Machine.Id == 0)
                {
                    Machine.Active = true; // Alterado de '1' para 'true'
                    Machine.Chance = 10;
                }

                var normalTasks = Machine.Items.Select(async item =>
                {
                    if (item.ItemId > 0)
                    {
                        var itemQuery = await Sender.Send(new GetItemAssetByIdQuery(item.ItemId));
                        if (itemQuery?.Register != null)
                            item.Name = itemQuery.Register.Name;
                    }
                }).ToList();

                var rareTasks = Machine.RareItems.Select(async rare =>
                {
                    if (rare.RareItem > 0)
                    {
                        var itemQuery = await Sender.Send(new GetItemAssetByIdQuery(rare.RareItem));
                        if (itemQuery?.Register != null)
                            rare.Name = itemQuery.Register.Name;
                    }
                }).ToList();

                await Task.WhenAll(normalTasks.Concat(rareTasks));
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error loading machine: {ex.Message}", Severity.Error);
                Navigation.NavigateTo("/machines");
            }
            finally
            {
                Loading = false;
                StateHasChanged();
            }
        }



        private async Task SaveMachine()
        {
            if (Machine == null) return;

            try
            {
                Saving = true;
                StateHasChanged();

                foreach (var item in Machine.Items.Where(i => i.ItemId > 0))
                {
                    var itemQuery = await Sender.Send(new GetItemAssetByIdQuery(item.ItemId));
                    if (itemQuery?.Register != null)
                        item.Name = itemQuery.Register.Name;
                }

                foreach (var rare in Machine.RareItems.Where(r => r.RareItem > 0))
                {
                    var itemQuery = await Sender.Send(new GetItemAssetByIdQuery(rare.RareItem));
                    if (itemQuery?.Register != null)
                        rare.Name = itemQuery.Register.Name;
                }

                var command = new UpdateGotchaAssetCommand(Machine);
                var result = await Sender.Send(command);

                Snackbar.Add(result ? "Machine saved successfully!" : "Failed to save machine",
                            result ? Severity.Success : Severity.Error);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error saving machine: {ex.Message}", Severity.Error);
            }
            finally
            {
                Saving = false;
                StateHasChanged();
            }
        }

        private void AddNewNormalItem()
        {
            if (Machine == null) return;

            Machine.Items.Add(new GotchaItemsAssetDTO
            {
                ItemId = 0,
                ItemCount = 1,
                InitialQuanty = 1,
                Quanty = 1,
                GotchaId = Machine.Id,
                Name = "New Normal Item"
            });
            StateHasChanged();
        }

        private void RemoveNormalItem(GotchaItemsAssetDTO item)
        {
            Machine?.Items.Remove(item);
            StateHasChanged();
        }

        private void AddNewRareItem()
        {
            if (Machine == null) return;

            Machine.RareItems.Add(new GotchaRareItemsAssetDTO
            {
                RareItem = 0,
                RareItemCnt = 1,
                RareItemGive = 1,
                GotchaId = Machine.Id,
                Name = "New Rare Item"
            });
            StateHasChanged();
        }

        private void RemoveRareItem(GotchaRareItemsAssetDTO rareItem)
        {
            Machine?.RareItems.Remove(rareItem);
            StateHasChanged();
        }
    }
}