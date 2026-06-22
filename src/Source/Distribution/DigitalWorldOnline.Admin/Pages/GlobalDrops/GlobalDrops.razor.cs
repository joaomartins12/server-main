using Microsoft.AspNetCore.Components;
using DigitalWorldOnline.Admin.Shared;
using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Commons.Enums.Admin;
using DigitalWorldOnline.Commons.ViewModel.GlobalDrops;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using MudBlazor;
using MediatR;

namespace DigitalWorldOnline.Admin.Pages.GlobalDrops
{
    public partial class GlobalDrops
    {
        [Inject]
        public ISender Sender { get; set; }

        [Inject]
        public IMapper Mapper { get; set; }

        [Inject]
        public NavigationManager Nav { get; set; }

        [Inject]
        IDialogService DialogService { get; set; }

        private MudTextField<string> _filterParameter;
        private MudTable<GlobalDropsViewModel> _table;

        private async Task<TableData<GlobalDropsViewModel>> ServerReload(TableState state)
        {
            var globaldrop = await Sender.Send(
                new GetGlobalDropsConfigsQuery(
                    state.Page,
                    state.PageSize,
                    state.SortLabel,
                    (SortDirectionEnum)state.SortDirection.GetHashCode(),
                    _filterParameter?.Value
                )
            );

            var pageData = Mapper.Map<IEnumerable<GlobalDropsViewModel>>(globaldrop.Registers);

            return new TableData<GlobalDropsViewModel>() { TotalItems = globaldrop.TotalRegisters, Items = pageData };
        }

        private void Create()
        {
            Nav.NavigateTo($"/globaldrops/create");
        }

        private void Update(long id)
        {
            Nav.NavigateTo($"/globaldrops/update/{id}");
        }

        private async Task Delete(long id)
        {
            var parameters = new DialogParameters
            {
                { "ContentText", "Chrysalimon gonna use Data Crusher and wipe this data. Do you want to proceed? This process cannot be undone." }
            };

            var options = new DialogOptions() { CloseButton = true, MaxWidth = MaxWidth.ExtraSmall };

            var dialog = DialogService.Show<ConfirmDialog>("Delete", parameters, options);

            var result = await dialog.Result;

            if (!result.Cancelled)
                Nav.NavigateTo($"/globaldrops/delete/{id}");
            else
                await Refresh();
        }

        private async Task Refresh()
        {
            await _table.ReloadServerData();
            await new Task(() => { _table.Loading = false; });
        }
    }
}