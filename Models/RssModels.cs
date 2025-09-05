using System.Xml.Serialization;

namespace FireIncidents.Models
{
    [XmlRoot("rss")]
    public class RssFeed
    {
        [XmlAttribute("version")]
        public string Version { get; set; }

        [XmlElement("channel")]
        public RssChannel Channel { get; set; }
    }

    public class RssChannel
    {
        [XmlElement("title")]
        public string Title { get; set; }

        [XmlElement("link")]
        public string Link { get; set; }

        [XmlElement("description")]
        public string Description { get; set; }

        [XmlElement("language")]
        public string Language { get; set; }

        [XmlElement("ttl")]
        public int Ttl { get; set; }

        [XmlElement("image")]
        public RssImage Image { get; set; }

        [XmlElement("item")]
        public List<RssItem> Items { get; set; } = new List<RssItem>();
    }

    public class RssImage
    {
        [XmlElement("title")]
        public string Title { get; set; }

        [XmlElement("link")]
        public string Link { get; set; }

        [XmlElement("url")]
        public string Url { get; set; }

        [XmlElement("width")]
        public int Width { get; set; }

        [XmlElement("height")]
        public int Height { get; set; }
    }

    public class RssItem
    {
        [XmlElement("title")]
        public string Title { get; set; }

        [XmlElement("description")]
        public string Description { get; set; }

        [XmlElement("pubDate")]
        public string PubDateString { get; set; }

        [XmlElement("guid")]
        public string Guid { get; set; }

        [XmlElement("link")]
        public string Link { get; set; }

        [XmlElement("creator", Namespace = "http://purl.org/dc/elements/1.1/")]
        public string Creator { get; set; }

        // Parsed publication date
        [XmlIgnore]
        public DateTime PubDate
        {
            get
            {
                if (DateTimeOffset.TryParse(PubDateString, out DateTimeOffset result))
                    return result.DateTime;
                if (DateTime.TryParse(PubDateString, out DateTime fallbackResult))
                    return fallbackResult;
                return DateTime.MinValue;
            }
        }

        // Extract plain text from HTML description
        [XmlIgnore]
        public string PlainTextDescription
        {
            get
            {
                if (string.IsNullOrEmpty(Description))
                    return string.Empty;

                // Remove CDATA wrapper
                var text = Description;
                if (text.StartsWith("<![CDATA[") && text.EndsWith("]]>"))
                {
                    text = text.Substring(9, text.Length - 12);
                }

                // Remove HTML tags using regex
                text = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", string.Empty);
                
                // Decode HTML entities
                text = System.Net.WebUtility.HtmlDecode(text);
                
                // Clean up extra whitespace
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
                
                return text;
            }
        }
    }
}