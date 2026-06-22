using DigitalWorldOnline.Application.Separar.Commands.Update;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Commons.Packets.Chat;
using MediatR;
using Serilog;  // Adicione esta linha no topo do arquivo

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class HatchRemoveEggPacketProcessor : IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.HatchRemoveEgg;

        private readonly AssetsLoader _assets;
        private readonly ISender _sender;
        private readonly ILogger _logger;  // Agora, o ILogger será reconhecido

        public HatchRemoveEggPacketProcessor(
            AssetsLoader assets,
            ISender sender,
            ILogger logger)
        {
            _assets = assets;
            _sender = sender;
            _logger = logger;
        }

        public async Task Process(GameClient client, byte[] packetData)
        {
            var incubator = client.Tamer.Incubator;

            await incubator.Lock.WaitAsync(); // ← trava concorrência
            try
            {
                if (incubator.NotDevelopedEgg)
                {
                    // _logger.Information("Tentando remover o ovo da incubadora...");

                    var newItem = new ItemModel();
                    newItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == incubator.EggId));
                    newItem.SetItemId(incubator.EggId);
                    newItem.SetAmount(1);

                    var cloneItem = (ItemModel)newItem.Clone();

                    if (client.Tamer.Inventory.AddItem(cloneItem))
                    {
                        await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
                        // _logger.Information("Ovo adicionado ao inventário.");
                    }
                    else
                    {
                        _logger.Warning($"Inventário cheio para recuperação do item {incubator.EggId}.");
                        client.Send(new SystemMessagePacket($"Inventário cheio para recuperação do item {incubator.EggId}."));
                        return;
                    }

                    incubator.RemoveEgg();
                    // _logger.Information("Ovo removido da incubadora com sucesso.");
                }
                else
                {
                    _logger.Warning("Não há ovo na incubadora para remover ou o ovo já foi desenvolvido.");
                    client.Send(new SystemMessagePacket("Não há ovo na incubadora para remover ou o ovo já foi desenvolvido."));
                    return;
                }

                await _sender.Send(new UpdateIncubatorCommand(incubator));
            }
            finally
            {
                incubator.Lock.Release(); // ← libera acesso
            }
        }


    }
}
