using DigitalWorldOnline.Application.Admin.Commands;
using DigitalWorldOnline.Application.Admin.Queries;
using DigitalWorldOnline.Commons.DTOs.Character;
using DigitalWorldOnline.Commons.DTOs.Digimon;
using DigitalWorldOnline.Commons.Enums.Admin;
using DigitalWorldOnline.Commons.Enums.Character;
using DigitalWorldOnline.Commons.ViewModel.Players;
using MediatR;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DigitalWorldOnline.Admin.Pages.Players
{
    public partial class PlayerEdit
    {
        [Parameter] public long PlayerId { get; set; }

        [Inject] public ISender Sender { get; set; }
        [Inject] public ISnackbar Snackbar { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }

        private PlayerViewModel? Player;
        private bool Loading = true;
        private bool Saving = false;
        private bool success;
        private MudForm form;

        protected override async Task OnInitializedAsync()
        {
            await LoadPlayer();
        }
        private async Task RemoveDigimon(DigimonDTO digimon)
        {
            if (Player?.Digimons != null)
            {
                Player.Digimons.Remove(digimon);
                await SavePlayer(); // Salva imediatamente
            }
        }

        private async Task AddNewDigimon()
        {
            if (Player?.Digimons != null)
            {
                Player.Digimons.Add(new DigimonDTO
                {
                    Name = "New Digimon",
                    Level = 1,
                    CurrentExperience = 0,
                    CurrentHp = 100,
                    CurrentDs = 50
                });

                await SavePlayer(); // Salva imediatamente
            }
        }

        private short _questIdToReset;
        private bool _resettingQuests;

       

        private async Task SaveDigimons()
        {
            if (Player == null) return;

            Saving = true;
            try
            {
                var command = new UpdatePlayerCommand(
                    Player.Id,
                    Player.Name,
                    Player.Level,
                    Player.CurrentExperience,
                    Player.MapId,
                    Player.State,
                    Player.EventState,
                    Player.Channel,
                    Player.Model,
                    Player.Size,
                    Player.CurrentHp,
                    Player.CurrentDs,
                    Player.XGauge,
                    Player.XCrystals,
                    Player.CurrentTitle,
                    Player.DigimonSlots,
                    Player.Digimons // <- Aqui você garante que está enviando a lista atualizada
                );

                var result = await Sender.Send(command);

                if (result)
                {
                    Snackbar.Add("Digimons salvos com sucesso!", Severity.Success);
                }
                else
                {
                    Snackbar.Add("Falha ao salvar os Digimons.", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Erro ao salvar Digimons: {ex.Message}", Severity.Error);
            }
            finally
            {
                Saving = false;
            }
        }




        private async Task LoadPlayer()
        {
            try
            {
                Loading = true;

                // Get player details from database
                var result = await Sender.Send(new GetPlayerByIdQuery(PlayerId));

                if (result?.Register != null)
                {
                    Player = new PlayerViewModel
                    {
                        Id = result.Register.Id,
                        AccountId = result.Register.AccountId,
                        Name = result.Register.Name,
                        Level = result.Register.Level,
                        CurrentExperience = result.Register.CurrentExperience,
                        MapId = result.Register.Location?.MapId ?? 0,
                        State = result.Register.State,
                        EventState = result.Register.EventState,
                        Channel = result.Register.Channel,
                        Model = result.Register.Model,
                        Size = result.Register.Size,
                        CurrentHp = result.Register.CurrentHp,
                        CurrentDs = result.Register.CurrentDs,
                        XGauge = result.Register.Xai?.XGauge ?? 0,
                        XCrystals = result.Register.Xai?.XCrystals ?? 0,
                        CurrentTitle = result.Register.CurrentTitle,
                        DigimonSlots = result.Register.DigimonSlots,
                        Position = result.Register.Position,
                        CreateDate = result.Register.CreateDate,

                        Digimons = result.Register.Digimons?.Select(d => new DigimonDTO
                        {
                            Id = d.Id,
                            BaseType = d.BaseType,
                            Model = d.Model,
                            Level = d.Level,
                            Name = d.Name,
                            Size = d.Size,
                            CurrentExperience = d.CurrentExperience,
                            CurrentSkillExperience = d.CurrentSkillExperience,
                            TranscendenceExperience = d.TranscendenceExperience,
                            CreateDate = d.CreateDate,
                            HatchGrade = d.HatchGrade,
                            CurrentType = d.CurrentType,
                            Friendship = d.Friendship,
                            CurrentHp = d.CurrentHp,
                            CurrentDs = d.CurrentDs,
                            Slot = d.Slot,
                            CharacterId = d.CharacterId,
                            BuffList = d.BuffList,
                            Evolutions = d.Evolutions,
                            Digiclone = d.Digiclone,
                            AttributeExperience = d.AttributeExperience,
                            Location = d.Location,

                        }).ToList() ?? new List<DigimonDTO>(),

                        // Progresso do personagem
                        // Ajuste para CharacterProgresses:
                        CharacterProgresses = result.Register.Progress != null ?
                    new List<CharacterProgressDTO> {
                        new CharacterProgressDTO
                        {
                            // Copia todas as propriedades existentes
                            Id = result.Register.Progress.Id,
                            CharacterId = result.Register.Progress.CharacterId,
                            CompletedData = result.Register.Progress.CompletedData,
                            CompletedDataValue = result.Register.Progress.CompletedDataValue,
            
                            // Carrega as quests apenas se existirem
                            InProgressQuestData = result.Register.Progress.InProgressQuestData?
                                .Where(q => q != null)
                                .ToList() ?? new List<InProgressQuestDTO>()
                        }
                    }
                    : new List<CharacterProgressDTO>(),

                    };
                }
                else
                {
                    Snackbar.Add("Player not found", Severity.Error);
                    Navigation.NavigateTo("/players");
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error loading player: {ex.Message}", Severity.Error);
                Navigation.NavigateTo("/players");
            }
            finally
            {
                Loading = false;
            }
        }

        private short _questIdToUpdate;
        private bool _isUpdatingQuest;
        private bool _questCompletionStatus;

        private async Task UpdateQuestStatus()
        {
            if (Player == null || _questIdToUpdate <= 0) return;

            try
            {
                _isUpdatingQuest = true;
                var result = await Sender.Send(new UpdatePlayerQuestsCommand(
                 characterId: Player.Id, // Usando o nome correto do parâmetro
                 questId: _questIdToUpdate,
                 isCompleted: _questCompletionStatus
             ));

                if (result)
                {
                    Snackbar.Add($"Quest {_questIdToUpdate} atualizada com sucesso!", Severity.Success);
                    await LoadPlayer();
                }
                else
                {
                    Snackbar.Add("Falha ao atualizar a quest", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Erro ao atualizar quest: {ex.Message}", Severity.Error);
            }
            finally
            {
                _isUpdatingQuest = false;
            }
        }

        private bool _deletingQuests;

        private async Task DeleteActiveQuests()
        {
            if (Player == null) return;

            try
            {
                _deletingQuests = true;

                var command = new DeletePlayerActiveQuestsCommand(Player.Id);

                var result = await Sender.Send(command);

                if (result)
                {
                    Snackbar.Add("Quests ativas deletadas com sucesso!", Severity.Success);
                    await LoadPlayer();
                }
                else
                {
                    Snackbar.Add("Falha ao deletar quests ativas.", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Erro ao deletar quests: {ex.Message}", Severity.Error);
            }
            finally
            {
                _deletingQuests = false;
            }
        }

        private bool IsQuestCompleted(CharacterProgressDTO progress, short questId)
        {
            if (progress?.CompletedDataValue == null)
                return false;

            try
            {
                // No need to split; CompletedDataValue is already an array of integers
                int intIndex = (questId - 1) / 32;
                int bitPosition = (questId - 1) % 32;

                if (intIndex >= progress.CompletedDataValue.Length)
                    return false;

                int bitmask = progress.CompletedDataValue[intIndex];

                return (bitmask & (1 << bitPosition)) != 0;
            }
            catch
            {
                return false;
            }
        }






        private async Task SavePlayer()
        {
            if (Player == null) return;

            try
            {
                Saving = true;

                var command = new UpdatePlayerCommand(
                    Player.Id,
                    Player.Name,
                    Player.Level,
                    Player.CurrentExperience,
                    Player.MapId,
                    Player.State,
                    Player.EventState,
                    Player.Channel,
                    Player.Model,
                    Player.Size,
                    Player.CurrentHp,
                    Player.CurrentDs,
                    Player.XGauge,
                    Player.XCrystals,
                    Player.CurrentTitle,
                    Player.DigimonSlots,
                    Player.Digimons // Digimons adicionados aqui
                );

                var result = await Sender.Send(command);

                if (result)
                {
                    Snackbar.Add("Player updated successfully!", Severity.Success);
                    Navigation.NavigateTo("/players");
                }
                else
                {
                    Snackbar.Add("Failed to update player", Severity.Error);
                }
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error saving player: {ex.Message}", Severity.Error);
            }
            finally
            {
                Saving = false;
            }
        }

    }
}
