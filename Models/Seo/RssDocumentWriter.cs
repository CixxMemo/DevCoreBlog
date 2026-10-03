using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Xml;
using DevCoreBlog.Core.ReadModels;
using DevCoreBlog.Routing;
using DevCoreBlog.Services.Interfaces;

namespace DevCoreBlog.Models.Seo;

/// <summary>Serializes a small RSS 2.0 document with plain summaries and trusted stable links.</summary>
public sealed class RssDocumentWriter(PublicUrlBuilder urls, ISafeMarkdownRenderer renderer)
{
    public byte[] Write(IReadOnlyList<RssPost> posts)
    {
        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false), Indent = true
        }))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("rss");
            xml.WriteAttributeString("version", "2.0");
            xml.WriteStartElement("channel");
            xml.WriteElementString("title", "DevCoreBlog");
            xml.WriteElementString("link", urls.AbsolutePath("/"));
            xml.WriteElementString("description", "Latest articles from DevCoreBlog.");
            xml.WriteStartElement("atom", "link", "http://www.w3.org/2005/Atom");
            xml.WriteAttributeString("href", urls.RssUrl());
            xml.WriteAttributeString("rel", "self");
            xml.WriteAttributeString("type", "application/rss+xml");
            xml.WriteEndElement();
            foreach (var post in posts)
            {
                var link = urls.PostUrl(post.Slug);
                xml.WriteStartElement("item");
                xml.WriteElementString("title", XmlText(post.Title));
                xml.WriteElementString("link", link);
                xml.WriteStartElement("guid");
                xml.WriteAttributeString("isPermaLink", "true");
                xml.WriteString(link); // F19 preserves slugs; editorial/date changes preserve identity.
                xml.WriteEndElement();
                xml.WriteElementString("pubDate", post.PublishDate.ToUniversalTime().ToString("r", CultureInfo.InvariantCulture));
                // RSS readers may interpret description as HTML after XML decoding.
                xml.WriteElementString("description", HtmlEncoder.Default.Encode(Summary(post)));
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
            xml.WriteEndElement();
            xml.WriteEndDocument();
        }
        return stream.ToArray();
    }

    private string Summary(RssPost post)
    {
        var text = XmlText(renderer.ToPlainText(post.Summary));
        if (string.IsNullOrWhiteSpace(text)) text = XmlText(renderer.ToPlainText(post.Excerpt));
        // Empty summaries stay empty; no invented author, language or article text.
        return string.Concat(text.EnumerateRunes().Take(320).Select(rune => rune.ToString()));
    }

    // Remove XML 1.0-incompatible legacy controls while preserving valid surrogate pairs.
    private static string XmlText(string value) => string.Concat(value.EnumerateRunes()
        .Where(rune => rune.Value >= 0x10000 || XmlConvert.IsXmlChar((char)rune.Value))
        .Select(rune => rune.ToString()));
}
