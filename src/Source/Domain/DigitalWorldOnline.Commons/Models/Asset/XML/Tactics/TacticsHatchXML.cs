using System.Xml.Serialization;

namespace DigitalWorldOnline.Commons.Models.Assets.XML.Tactics
{
    [XmlRoot("HatchList")]
    public class TacticHatchWrapper
    {
        [XmlElement("Hatch")]
        public List<TacticHatchAssetModel> Hatch { get; set; }
    }

    public class TacticHatchAssetModel
    {
        [XmlElement("ItemId")]
        public int ItemId { get; set; }

        [XmlElement("HatchType")]
        public int HatchType { get; set; }

        [XmlElement("LowClassDataSection")]
        public int LowClassDataSection { get; set; }

        [XmlElement("MidClassDataSection")]
        public int MidClassDataSection { get; set; }

        [XmlElement("LowClassDataAmount")]
        public int LowClassDataAmount { get; set; }

        [XmlElement("MidClassDataAmount")]
        public int MidClassDataAmount { get; set; }

        [XmlElement("LowClassLimitLevel")]
        public int LowClassLimitLevel { get; set; }

        [XmlElement("MidClassLimitLevel")]
        public int MidClassLimitLevel { get; set; }

        [XmlElement("LowClassBreakPoint")]
        public int LowClassBreakPoint { get; set; }

        [XmlElement("MidClassBreakPoint")]
        public int MidClassBreakPoint { get; set; }
    }
}