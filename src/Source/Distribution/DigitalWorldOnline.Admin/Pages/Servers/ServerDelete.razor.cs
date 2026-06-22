using DigitalWorldOnline.Application.Admin.Commands;
using MediatR;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using Serilog;
using System;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Admin.Pages.Servers
{
    public partial class ServerDelete
    {
        bool _loading;
        long _id;

        [Parameter]
        public string ServerId { get; set; }

        [Inject]
        public NavigationManager Nav { get; set; }

        [Inject]
        public ISender Sender { get; set; }

        [Inject]
        public ISnackbar Toast { get; set; }

        [Inject]
        public ILogger Logger { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            long.TryParse(ServerId, out _id);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);

            if (_id == 0)
            {
                Toast.Add("Server not found, try again later.", Severity.Warning);

                Return();
            }

            if (firstRender)
            {
                await Delete();
            }
        }

        private async Task Delete()
        {
            try
            {
                _loading = true;
                StateHasChanged();


                await Sender.Send(new DeleteServerCommand(_id));


                Toast.Add("Server deleted.", Severity.Success);

                Nav.NavigateTo("/servers");
            }
            catch (Exception ex)
            {
                Logger.Error("Error deleting server id {id}: {ex}", _id, ex.Message);
                Toast.Add("Unable to delete server, try again later.", Severity.Error);
            }
            finally
            {
                _loading = false;
                StateHasChanged();

                Return();
            }
        }

        private void Return()
        {
            Nav.NavigateTo("/servers");
        }
    }
}