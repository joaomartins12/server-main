using AutoMapper;
using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Commons.DTOs.Config;
using DigitalWorldOnline.Commons.ViewModel.GlobalDrops;
using MediatR;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Serilog;
using System;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Admin.Pages.GlobalDrops
{
    public partial class GlobalDropsUpdate
    {
        GlobalDropsViewModel _globaldrops = new GlobalDropsViewModel();
        bool Loading = false;
        long _id;

        [Parameter]
        public string GlobalDropId { get; set; }

        [Inject]
        public NavigationManager Nav { get; set; }

        [Inject]
        public ISender Sender { get; set; }

        [Inject]
        public IMapper Mapper { get; set; }

        [Inject]
        public ISnackbar Toast { get; set; }

        [Inject]
        public ILogger Logger { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            if (long.TryParse(GlobalDropId, out _id))
            {
                Logger.Information("Searching globalDrops config by id {id}", _id);

                var target = await Sender.Send(new GetGlobalDropsConfigByIdQuery(_id));

                if (target.Register == null)
                    _id = 0;
                else
                {
                    _globaldrops = Mapper.Map<GlobalDropsViewModel>(target.Register);
                }
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);

            if (_id == 0)
            {
                Logger.Information("Invalid globalDrops config id parameter: {parameter}", GlobalDropId);
                Toast.Add("GlobalDrops config not found, try again later.", Severity.Warning);

                Return();
            }

            StateHasChanged();
        }

        private async Task Update()
        {
            try
            {
                /*if (_globaldrops.Invalid)
                {
                    Toast.Add("Invalid globalDrops configuration.", Severity.Warning);

                    return;
                }*/

                Loading = true;

                StateHasChanged();

                Logger.Information("Updating globalDrops config id {id}", _globaldrops.Id);

                var configDto = Mapper.Map<GlobalDropsConfigDTO>(_globaldrops);

                await Sender.Send(new UpdateGlobalDropsConfigCommand(configDto));

                Logger.Information("GlobalDrops config id {id} update", _globaldrops.Id);

                Toast.Add("GlobalDrops config updated successfully.", Severity.Success);

                Return();
            }
            catch (Exception ex)
            {
                Logger.Error("Error updating globalDrops config with id {id}: {ex}", _globaldrops.Id, ex.Message);
                Toast.Add("Unable to update globalDrops config, try again later.", Severity.Error);
                Return();
            }
            finally
            {
                Loading = false;

                StateHasChanged();
            }
        }

        private void Return()
        {
            Nav.NavigateTo($"/globaldrops");
        }
    }
}