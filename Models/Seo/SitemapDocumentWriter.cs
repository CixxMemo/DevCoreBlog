using System.Globalization;
using System.Text;
using System.Xml;

namespace DevCoreBlog.Models.Seo;

/// <summary>Serializes already selected canonical URLs as UTF-8 sitemap bytes.</summary>
public static class SitemapDocumentWriter
{
    public const int MaximumUrls = 50_000;
    public const int MaximumBytes = 52_428_800;

    public static byte[] Write(IEnumerable<(string Url, DateTime? LastModified)> entries)
    {
        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true
        }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            foreach (var (url, lastModified) in entries)
            {
                xml.WriteStartElement("url");
                xml.WriteElementString("loc", url);
                if (lastModified is { } date)
                    xml.WriteElementString("lastmod", date.ToString("O", CultureInfo.InvariantCulture));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }
        return stream.ToArray();
    }
}
