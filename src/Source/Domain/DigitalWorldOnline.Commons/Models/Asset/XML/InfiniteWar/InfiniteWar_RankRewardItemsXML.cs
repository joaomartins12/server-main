using System.Xml.Serialization;

namespace DigitalWorldOnline.Commons.Models.Assets.XML.InfiniteWar
{
    [XmlRoot("RankRewardItemsList")]
    public class InfiniteWar_RankRewardItemsWrapper
    {
        [XmlElement("RankRewardItemsContanier")]
        public List<InfiniteWar_RankRewardItemsXmlModel> InfiniteWar_RankRewardItems { get; set; }

        public InfiniteWar_RankRewardItemsWrapper()
        {
            InfiniteWar_RankRewardItems = new List<InfiniteWar_RankRewardItemsXmlModel>();
        }
    }

    public class InfiniteWar_RankRewardItemsXmlModel
    {
        [XmlElement("nKeyValue")]
        public int RankRewardType { get; set; }

        [XmlElement("nSubCount")]
        public int RankRewardCount { get; set; }

        [XmlElement("RankRewardInfos")]
        public InfiniteWar_RankRewardInfosXmlModel InfiniteWar_RankRewardInfos { get; set; }

        public InfiniteWar_RankRewardItemsXmlModel()
        {
            InfiniteWar_RankRewardInfos = new InfiniteWar_RankRewardInfosXmlModel();
        }
    }

    public class InfiniteWar_RankRewardInfosXmlModel
    {
        [XmlElement("RankRewardInfo")]
        public List<InfiniteWar_RankRewardInfoModel> InfiniteWar_RankRewardInfo { get; set; }

        public InfiniteWar_RankRewardInfosXmlModel()
        {
            InfiniteWar_RankRewardInfo = new List<InfiniteWar_RankRewardInfoModel>();
        }
    }

    public class InfiniteWar_RankRewardInfoModel
    {
        [XmlElement("s_nRankType")]
        public int RankType { get; set; }

        [XmlElement("s_nRankMin")]
        public int RankMin { get; set; }

        [XmlElement("s_nRankMax")]
        public int RankMax { get; set; }

        [XmlElement("nItemCount")]
        public int RankItemCount { get; set; }

        [XmlElement("RankRewardItemsInfo")]
        public InfiniteWar_RankRewardItemsInfoXmlModel InfiniteWar_RankRewardItemsInfo { get; set; }

        public InfiniteWar_RankRewardInfoModel()
        {
            InfiniteWar_RankRewardItemsInfo = new InfiniteWar_RankRewardItemsInfoXmlModel();
        }
    }

    public class InfiniteWar_RankRewardItemsInfoXmlModel
    {
        [XmlElement("RankRewardItem")]
        public List<InfiniteWar_RankRewardItemXmlModel> InfiniteWar_RankRewardItem { get; set; }

        public InfiniteWar_RankRewardItemsInfoXmlModel()
        {
            InfiniteWar_RankRewardItem = new List<InfiniteWar_RankRewardItemXmlModel>();
        }
    }

    public class InfiniteWar_RankRewardItemXmlModel
    {
        [XmlElement("s_dwItemCode")]
        public int ItemId { get; set; }

        [XmlElement("s_nCount")]
        public int ItemAmount { get; set; }
    }
}