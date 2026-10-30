using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

namespace NewsletterCombiner.Core;

internal static class PdfImagePlacement
{
    internal record Size(double Width, double Height);
    readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static Matrix Identity => new(1, 0, 0, 1, 0, 0);
        public Matrix Then(Matrix m) => new(A * m.A + C * m.B, B * m.A + D * m.B,
            A * m.C + C * m.D, B * m.C + D * m.D, A * m.E + C * m.F + E, B * m.E + D * m.F + F);
    }
    static PdfItem? Resolve(PdfItem? value) => value is PdfReference r ? r.Value : value;
    static double Number(CObject value) => value switch { CReal r => r.Value, CInteger i => i.Value, _ => throw new InvalidDataException("Invalid image transform.") };
    static double Number(PdfItem value) => Resolve(value) switch { PdfReal r => r.Value, PdfInteger i => i.Value, _ => throw new InvalidDataException("Invalid form transform.") };

    internal static Dictionary<PdfDictionary, Size> Find(PdfDocument pdf, CancellationToken token)
    {
        var result = new Dictionary<PdfDictionary, Size>();
        foreach (var page in pdf.Pages)
        {
            token.ThrowIfCancellationRequested();
            var perPage = new Dictionary<PdfDictionary, Size>();
            try
            {
                var unit = page.Elements.ContainsKey("/UserUnit") ? page.Elements.GetReal("/UserUnit") : 1d;
                Visit(ContentReader.ReadContent(page), page.Elements.GetDictionary("/Resources"), new(unit, 0, 0, unit, 0, 0), perPage, [], token);
            }
            catch (Exception e) when (e is not OperationCanceledException && e is not OutOfMemoryException)
            {
                // An unknown placement could be larger than a known one. Disable DPI estimates globally if parsing fails.
                return [];
            }
            foreach (var (image, size) in perPage) Record(result, image, size);
        }
        return result;
    }

    static void Record(Dictionary<PdfDictionary, Size> sizes, PdfDictionary image, Size size)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0) return;
        if (sizes.TryGetValue(image, out var old)) size = new(Math.Max(old.Width, size.Width), Math.Max(old.Height, size.Height));
        sizes[image] = size;
    }

    static void Visit(CSequence content, PdfDictionary? resources, Matrix matrix, Dictionary<PdfDictionary, Size> sizes,
        HashSet<PdfDictionary> forms, CancellationToken token)
    {
        var stack = new Stack<Matrix>();
        foreach (var op in content.OfType<COperator>())
        {
            token.ThrowIfCancellationRequested();
            switch (op.OpCode.Name)
            {
                case "q": stack.Push(matrix); break;
                case "Q": matrix = stack.Pop(); break;
                case "cm":
                    matrix = matrix.Then(new(Number(op.Operands[0]), Number(op.Operands[1]), Number(op.Operands[2]),
                        Number(op.Operands[3]), Number(op.Operands[4]), Number(op.Operands[5])));
                    break;
                case "Do":
                    if (op.Operands[0] is not CName name) break;
                    var xobjects = resources?.Elements.GetDictionary("/XObject");
                    var obj = xobjects?.Elements.GetDictionary(name.Name);
                    if (obj == null) break;
                    if (obj.Elements.GetName("/Subtype") == "/Image")
                        Record(sizes, obj, new(Math.Sqrt(matrix.A * matrix.A + matrix.B * matrix.B), Math.Sqrt(matrix.C * matrix.C + matrix.D * matrix.D)));
                    else if (obj.Elements.GetName("/Subtype") == "/Form" && obj.Stream != null)
                    {
                        if (forms.Count >= 30 || !forms.Add(obj)) throw new InvalidDataException("Recursive image resource.");
                        var transform = Matrix.Identity;
                        if (obj.Elements.GetArray("/Matrix") is { Elements.Count: 6 } m)
                            transform = new(Number(m.Elements[0]), Number(m.Elements[1]), Number(m.Elements[2]), Number(m.Elements[3]), Number(m.Elements[4]), Number(m.Elements[5]));
                        Visit(ContentReader.ReadContent(obj.Stream.UnfilteredValue), obj.Elements.GetDictionary("/Resources") ?? resources, matrix.Then(transform), sizes, forms, token);
                        forms.Remove(obj);
                    }
                    break;
            }
        }
    }
}
