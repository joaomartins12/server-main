using AutoMapper;
using DigitalWorldOnline.Application.Admin.Commands;
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
    public partial class GlobalDropsCreation
    {
        GlobalDropsViewModel _globaldrops = new GlobalDropsViewModel();

        bool Loading = false;

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

        private async Task Create()
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

                Logger.Information("Creating new _globaldrops config.");

                var newConfig = Mapper.Map<GlobalDropsConfigDTO>(_globaldrops);

                await Sender.Send(new CreateGlobalDropsConfigCommand(newConfig));

                Logger.Information("GlobalDrops config created.");
                Toast.Add("GlobalFrops config created successfully.", Severity.Success);

                Return();
            }
            catch (Exception ex)
            {
                Logger.Error("Error creating globalDrops config: {ex}", ex.Message);
                Toast.Add("Unable to create globalDrops config, try again later.", Severity.Error);

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