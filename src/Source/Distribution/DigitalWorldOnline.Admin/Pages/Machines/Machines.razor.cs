using DigitalWorldOnline.Application.Separar.Queries;
using DigitalWorldOnline.Commons.DTOs.Assets;
using MediatR;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Admin.Pages.Machines
{
    public class MachinesBase : ComponentBase
    {
        [Inject]
        protected ISender Sender { get; set; }

        [Inject]
        protected NavigationManager Navigation { get; set; }

        [Inject]
        protected ISnackbar Snackbar { get; set; }

        public List<GotchaAssetDTO>? GotchaMachines;

        protected bool Loading = true;

        protected override async Task OnInitializedAsync()
        {
            await LoadMachines();
        }

        protected async Task LoadMachines()
        {
            try
            {
                Loading = true;
                GotchaMachines = await Sender.Send(new GotchaAssetsQuery());
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Failed to load machines: {ex.Message}", Severity.Error);
            }
            finally
            {
                Loading = false;
            }
        }
    }
}
