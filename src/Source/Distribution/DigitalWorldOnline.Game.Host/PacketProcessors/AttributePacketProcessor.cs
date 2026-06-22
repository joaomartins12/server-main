using System;
using System.Threading.Tasks;
using AutoMapper;
using DigitalWorldOnline.Commons.Enums.ClientEnums;
using DigitalWorldOnline.Commons.Enums.PacketProcessor;
using DigitalWorldOnline.Commons.Interfaces;
using DigitalWorldOnline.Commons.Models.Digimon;
using DigitalWorldOnline.Commons.Packets.GameServer;
using DigitalWorldOnline.Commons.Models.Config;
using DigitalWorldOnline.Commons.Models.Asset;
using MediatR;
using Serilog;
using DigitalWorldOnline.Commons.Entities;
using DigitalWorldOnline.Commons.Models.Base;
using DigitalWorldOnline.Application;
using DigitalWorldOnline.Commons.Models.Maps;
using System.Linq;
using DigitalWorldOnline.Application.Separar.Commands.Update;
using Microsoft.Extensions.Hosting;
using DigitalWorldOnline.Commons.Packets.Items;

namespace DigitalWorldOnline.Game.PacketProcessors
{
    public class AttributeExpPacketProcessor :IGamePacketProcessor
    {
        public GameServerPacketEnum Type => GameServerPacketEnum.AttributeItem;

        private readonly ILogger _logger;
        private readonly ISender _sender;
        private readonly IMapper _mapper;
        private readonly AssetsLoader _assets;

        public AttributeExpPacketProcessor(
            ILogger logger,
            ISender sender,
            IMapper mapper,
            AssetsLoader assets)
        {
            _logger = logger;
            _sender = sender;
            _mapper = mapper;
            _assets = assets;
        }

        public async Task Process(GameClient client,byte[] packetData)
        {
            var packet = new GamePacketReader(packetData);
            byte clickType = packet.ReadByte(); 
            byte rewardType = packet.ReadByte(); 
            short exp = packet.ReadShort();

            var digimonAttributeExp = client.Tamer.Partner.GetAttributeExperience();
            var digimonElementExp = client.Tamer.Partner.GetElementExperience();
            var digiAtt = client.Tamer.Partner.BaseInfo.Attribute;
            var digiEle = client.Tamer.Partner.BaseInfo.Element;

            if (digimonAttributeExp == 10000 && clickType == 0)
            {
                int[] attributeRewardItems = GetRewardItemsForAttribute(digiAtt);

                await GiveItemsOnExpDecrease(client,-10000,
                    () => client.Tamer.Partner.GetAttributeExperience(),
                    (expDecrease) => client.Tamer.Partner.AttributeExperience.IncreaseAttributeExperience((short)expDecrease,digiAtt),
                    attributeRewardItems
                );

            }
            if (digimonElementExp == 10000 && clickType == 1)
            {
                int[] elementRewardItems = GetRewardItemsForElement(digiEle);

                await GiveItemsOnExpDecrease(client,-10000,
                    () => client.Tamer.Partner.GetElementExperience(),
                    (expDecrease) => client.Tamer.Partner.AttributeExperience.IncreaseElementExperience((short)expDecrease,digiEle),
                    elementRewardItems
                );
            }

            client.Send(new NatureExpPacket(clickType,rewardType,(short)-10000));
        }

        public async Task GiveItemsOnExpDecrease(GameClient client,short expDecrease,Func<int> getCurrentExp,Action<int> decreaseExp,int[] rewardItemIds)
        {
            if (getCurrentExp() == 10000)
            {
                decreaseExp(expDecrease);

                await _sender.Send(new UpdateCharacterExperienceCommand(client.Tamer));
                await _sender.Send(new UpdateDigimonExperienceCommand(client.Tamer.Partner));

                List<ItemModel> rewardItems = new List<ItemModel>();

                foreach (var itemId in rewardItemIds)
                {
                    var rewardItem = new ItemModel();
                    rewardItem.SetItemInfo(_assets.ItemInfo.FirstOrDefault(x => x.ItemId == itemId));
                    rewardItem.SetItemId(itemId);
                    rewardItem.SetAmount(1);

                    rewardItems.Add(rewardItem);
                }

                rewardItems.ForEach(receivedItem =>
                {
                    client.Tamer.Inventory.AddItem(receivedItem);
                });

                await _sender.Send(new UpdateItemsCommand(client.Tamer.Inventory));
            }
        }

        private int[] GetRewardItemsForAttribute(DigimonAttributeEnum attribute)
        {
            switch (attribute)
            {
                case DigimonAttributeEnum.Data:
                    return new int[] { 28027 };
                case DigimonAttributeEnum.Vaccine:
                    return new int[] { 28028 };
                case DigimonAttributeEnum.Virus:
                    return new int[] { 28029 };
                case DigimonAttributeEnum.Unknown:
                    return new int[] { 28030 };
                default:
                    return new int[] { 99999 };
            }
        }

        private int[] GetRewardItemsForElement(DigimonElementEnum element)
        {
            switch (element)
            {
                case DigimonElementEnum.Ice:
                    return new int[] { 28016 };
                case DigimonElementEnum.Water:
                    return new int[] { 28017 };
                case DigimonElementEnum.Fire:
                    return new int[] { 28018 };
                case DigimonElementEnum.Land:
                    return new int[] { 28019 };
                case DigimonElementEnum.Wind:
                    return new int[] { 28020 };
                case DigimonElementEnum.Wood:
                    return new int[] { 28032 };
                case DigimonElementEnum.Light:
                    return new int[] { 28022 };
                case DigimonElementEnum.Dark:
                    return new int[] { 28023 };
                case DigimonElementEnum.Thunder:
                    return new int[] { 28024 };
                case DigimonElementEnum.Steel:
                    return new int[] { 28025 };
                default:
                    return new int[] { 28026 };
            }
        }
    }
}


