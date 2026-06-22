using System.Xml.Serialization;
using DigitalWorldOnline.Commons.Enums.ClientEnums;

namespace DigitalWorldOnline.Commons.Models.Assets.XML.ItemList
{
    [XmlRoot("ITEM")]
    public class itemlistAssetWrapper
    {
        [XmlElement("index")]
        public List<ItemListAssetModel> ItemList { get; set; }
    }

    public class ItemListAssetModel
    {
        [XmlElement("sINFO")]
        public List<ItemInfo> ItemList { get; set; }

        public ItemListAssetModel()
        {
            ItemList = new List<ItemInfo>();
        }
    }

    public class ItemInfo
    {
        [XmlElement("s_dwItemID")]
        public int ItemId { get; set; }

        [XmlElement("s_szName")]
        public string Name { get; set; }

        [XmlElement("s_nClass")]
        public int Class { get; set; }

        [XmlElement("s_nType_L")]
        public int Type { get; set; }

        [XmlElement("s_nSection")]
        public int Section { get; set; }

        [XmlElement("s_nSellType")]
        public int SellType { get; set; }

        [XmlElement("s_nBelonging")]
        public int BoundType { get; set; }

        [XmlElement("s_btUseTimeType")]
        public int UseTimeType { get; set; }

        [XmlElement("s_dwSkill")]
        public long SkillCode { get; set; }

        [XmlElement("s_nTamerReqMinLevel")]
        public byte TamerMinLevel { get; set; }

        [XmlElement("s_nTamerReqMaxLevel")]
        public byte TamerMaxLevel { get; set; }

        [XmlElement("s_nDigimonReqMinLevel")]
        public byte DigimonMinLevel { get; set; }

        [XmlElement("s_nDigimonReqMaxLevel")]
        public byte DigimonMaxLevel { get; set; }

        [XmlElement("s_dwSale")]
        public long SellPrice { get; set; }

        [XmlElement("s_dwScanPrice")]
        public int ScanPrice { get; set; }

        [XmlElement("s_dwDigiCorePrice")]
        public int DigicorePrice { get; set; }

        [XmlElement("s_nUseTime_Min")]
        public int UsageTimeMinutes { get; set; }

        [XmlElement("s_nOverlap")]
        public int Overlap { get; set; }

        [XmlElement("s_nUseCharacter")]
        //public int Target { get; set; }
        public ItemConsumeTargetEnum Target { get; set; }

        [XmlElement("s_dwEventItemPrice")]
        public int EventItemAmount { get; set; }

        [XmlElement("s_nEventItemType")]
        public int EventItemId { get; set; }

        [XmlElement("s_nTypeValue")]
        public int TypeN { get; set; }

        [XmlElement("s_btApplyRateMax")]
        public short ApplyValueMax { get; set; }

        [XmlElement("s_btApplyRateMin")]
        public short ApplyValueMin { get; set; }

        [XmlElement("s_btApplyElement")]
        public short ApplyElement { get; set; }

        // ------------------------------------------

        public SkillCodeAssetModel? SkillInfo { get; set; }
        public int TimeInSeconds => (UsageTimeMinutes * 60) + 10;
        public bool TemporaryItem => UsageTimeMinutes > 0;
        public void SetSkillInfo(SkillCodeAssetModel? skillCode) => SkillInfo ??= skillCode;

        // ------------------------------------------
    }
}