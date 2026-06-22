using System.Xml.Serialization;

namespace DigitalWorldOnline.Commons.Models.Assets.XML.MapObject
{
    public static class MapObjectReader
    {
        public static MapObjectsWrapper LoadFromXml(string xmlPath)
        {
            var serializer = new XmlSerializer(typeof(MapObjectsWrapper));
            using var stream = File.OpenRead(xmlPath);

            return (MapObjectsWrapper)serializer.Deserialize(stream);
        }
    }
}