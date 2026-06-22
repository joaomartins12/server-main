using System.Xml.Serialization;

namespace DigitalWorldOnline.Commons.Models.Assets.XML.MapObject
{
    [XmlRoot("MapObjects")]
    public class MapObjectsWrapper
    {
        [XmlElement("MapObject")]
        public List<MapObjectAssetModel> MapObjects { get; set; }
    }

    public class MapObjectAssetModel
    {
        [XmlElement("MapId")]
        public int MapId { get; set; }

        [XmlElement("Size")]
        public int Size { get; set; }

        [XmlElement("MapSourceObject")]
        public List<MapSourceObjectModel> MapSourceObjects { get; set; }

        public MapObjectAssetModel()
        {
            MapSourceObjects = new List<MapSourceObjectModel>();
        }
    }

    public class MapSourceObjectModel
    {
        [XmlElement("ObjectId")]
        public int ObjectId { get; set; }

        [XmlElement("ObjectCount")]
        public int ObjectCount { get; set; }

        [XmlElement("OrderObject")]
        public List<OrderObjectModel> OrderObjects { get; set; }

        public MapSourceObjectModel()
        {
            OrderObjects = new List<OrderObjectModel>();
        }
    }

    public class OrderObjectModel
    {
        [XmlElement("OrderId")]
        public int OrderId { get; set; }

        [XmlElement("FactorSize")]
        public int FactorSize { get; set; }

        [XmlElement("Object")]
        public List<ObjectModel> Objects { get; set; }

        public OrderObjectModel()
        {
            Objects = new List<ObjectModel>();
        }
    }

    public class ObjectModel
    {
        [XmlElement("s_nOpenType")]
        public int OpenType { get; set; }

        [XmlElement("s_nFactorCnt")]
        public int FactorCount { get; set; }

        [XmlElement("s_nFactor")]
        public int Factor { get; set; }
    }
}