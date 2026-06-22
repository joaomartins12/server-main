using System.Xml.Serialization;

namespace DigitalWorldOnline.Commons.Models.Assets.XML.Tactics
{
    public static class TacticHatchReader
    {
        public static TacticHatchWrapper LoadFromXml(string xmlPath)
        {
            var serializer = new XmlSerializer(typeof(TacticHatchWrapper));
            using var stream = File.OpenRead(xmlPath);

            return (TacticHatchWrapper)serializer.Deserialize(stream);
        }
    }
}