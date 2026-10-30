using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

using var pdf = PdfReader.Open(args[0], PdfDocumentOpenMode.Modify);
var images = pdf.Internals.GetAllObjects().OfType<PdfDictionary>().Where(x => x.Elements.GetName("/Subtype") == "/Image").ToArray();
var links = new List<string>();
foreach (var page in pdf.Pages)
{
    var annotations = page.Elements.GetArray("/Annots");
    if (annotations == null) continue;
    for (var i = 0; i < annotations.Elements.Count; i++)
    {
        var annotation = annotations.Elements.GetDictionary(i);
        if (annotation?.Elements.GetName("/Subtype") != "/Link") continue;
        links.Add(annotation.Elements.GetDictionary("/A")?.Elements.GetString("/URI") ?? "internal");
    }
}
Console.WriteLine(JsonSerializer.Serialize(new {
    Bytes = new FileInfo(args[0]).Length, Pages = pdf.PageCount, Links = links.Count,
    LinkFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', links)))),
    PageSizes = pdf.Pages.Cast<PdfPage>().Select(p => new { W = p.Width.Point, H = p.Height.Point }),
    PageContentFingerprints = pdf.Pages.Cast<PdfPage>().Select(p => Convert.ToHexString(SHA256.HashData(p.Contents.CreateSingleContent().Stream.UnfilteredValue))),
    ImageDimensions = images.GroupBy(x => new { W = x.Elements.GetInteger("/Width"), H = x.Elements.GetInteger("/Height") }).Select(g => new { g.Key, Count = g.Count() }),
    ImageTypes = images.GroupBy(x => new { Colour = x.Elements["/ColorSpace"]?.ToString(), Filter = x.Elements["/Filter"]?.ToString(), Bpc = x.Elements.GetInteger("/BitsPerComponent"), Mask = x.Elements.ContainsKey("/SMask"), Decode = x.Elements.ContainsKey("/Decode"), DecodeParms = x.Elements.ContainsKey("/DecodeParms") }).Select(g => new { g.Key, Count = g.Count(), Bytes = g.Sum(i => (long)(i.Stream?.Length ?? 0)) })
}, new JsonSerializerOptions { WriteIndented = true }));
