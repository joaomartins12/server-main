using AutoMapper;
using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Commons.Enums.Admin;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.ViewModel.Players;
using MediatR;
using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Admin.Pages
{
    public partial class Index : IDisposable
    {
        [Inject] public ISender Sender { get; set; }
        [Inject] public IMapper Mapper { get; set; }
        public bool Loading { get; set; } = false;

        public int OnlinePlayers { get; set; } = 0;
        public int MaxPlayers { get; set; } = 0;

        private Timer _timer;

        protected override async Task OnInitializedAsync()
        {
            // primeira carga
            await LoadPlayers();

            // atualizar a cada 10 segundos
            _timer = new Timer(async _ =>
            {
                await InvokeAsync(async () =>
                {
                    await LoadPlayers();
                    StateHasChanged();
                });
            }, null, 10000, 10000);
        }

        private async Task LoadPlayers()
        {
            try
            {
                var playersResult = await Sender.Send(
                    new GetPlayersQuery(
                        0,                          // primeira página
                        1000,                       // pageSize
                        "Id",                       // ordenar por Id
                        SortDirectionEnum.Asc,      // usa Ascendente
                        null                        // sem filtro
                    )
                );

                var players = Mapper.Map<IEnumerable<PlayerViewModel>>(playersResult.Registers);

                OnlinePlayers = players.Count(p =>
                    p.State == CharacterStateEnum.Ready ||
                    p.State == CharacterStateEnum.Loading);

                MaxPlayers = 1000; // capacidade máxima fixa
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Index] Erro a carregar players: {ex.Message}");
                OnlinePlayers = 0;
                MaxPlayers = 0;
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
