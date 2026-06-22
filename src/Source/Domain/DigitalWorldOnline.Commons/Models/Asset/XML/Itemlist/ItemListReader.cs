using System.Xml.Serialization;

namespace DigitalWorldOnline.Commons.Models.Assets.XML.ItemList
{
    public static class ItemListReader
    {
        public static itemlistAssetWrapper LoadFromXml(string xmlPath)
        {
            var serializer = new XmlSerializer(typeof(itemlistAssetWrapper));
            using var stream = File.OpenRead(xmlPath);

            return (itemlistAssetWrapper)serializer.Deserialize(stream);
        }
    }
}