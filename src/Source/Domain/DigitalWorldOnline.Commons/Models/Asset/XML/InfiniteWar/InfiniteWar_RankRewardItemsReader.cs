using System.Xml.Serialization;

namespace DigitalWorldOnline.Commons.Models.Assets.XML.InfiniteWar
{
    public static class InfiniteWar_RankRewardItemsReader
    {
        public static InfiniteWar_RankRewardItemsWrapper LoadFromXml(string xmlPath)
        {
            var serializer = new XmlSerializer(typeof(InfiniteWar_RankRewardItemsWrapper));
            using var stream = File.OpenRead(xmlPath);

            return (InfiniteWar_RankRewardItemsWrapper)serializer.Deserialize(stream);
        }
    }
}