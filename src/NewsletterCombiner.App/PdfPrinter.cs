using System.ComponentModel;
using System.IO;
using System.Printing;
using System.Printing.Interop;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NewsletterCombiner.Core;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace NewsletterCombiner.App;

internal sealed record PrinterSelection(string Server, string Queue, string FullName, byte[] DevMode)
{
    public static PrinterSelection? Choose()
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = false, SelectedPagesEnabled = false, CurrentPageEnabled = false };
        if (dialog.ShowDialog() != true) return null;
        using var queue = dialog.PrintQueue;
        if (queue == null) throw new InvalidOperationException("Choose an installed printer.");
        if (queue.QueuePort.Name is "PORTPROMPT:" or "FILE:")
            throw new InvalidOperationException("Choose a printer that prints to paper. The app already saves a PDF for every output.");
        return FromQueue(queue, dialog.PrintTicket);
    }

    internal static PrinterSelection FromQueue(PrintQueue queue, PrintTicket ticket)
    {
        var validated = queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket, ticket).ValidatedPrintTicket;
        using var converter = new PrintTicketConverter(queue.FullName, 1);
        return new(queue.HostingPrintServer.Name, queue.Name, queue.FullName,
            converter.ConvertPrintTicketToDevMode(validated, BaseDevModeType.UserDefault));
    }
}

internal static class PdfPrinter
{
    internal static Task<PrintResult> PrintAsync(IReadOnlyList<string> paths, PrinterSelection printer,
        IProgress<PrintProgress>? progress, CancellationToken token,
        Func<string, string?>? outputFileForCheck = null) => OnWorker(() =>
    {
        try
        {
            using var spooler = new WindowsPdfSpooler(printer, outputFileForCheck);
            return PrintBatch.RunAsync(paths, spooler, progress, token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return new PrintResult(0, paths.Count, true); }
        catch (Exception ex) { return new PrintResult(0, paths.Count, false, ex.Message); }
    });

    internal static Task<T> OnWorker<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(action()); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "GoodSort PDF printing" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}

// One PDF at a time, with at most two outstanding jobs from this batch. No external PDF reader.
internal sealed class WindowsPdfSpooler : IPdfPrintSpooler, IDisposable
{
    readonly PrinterSelection selection;
    readonly PrintServer server;
    readonly PrintQueue queue;
    readonly HashSet<int> pending = [];
    readonly Func<string, string?>? outputFileForCheck;
    internal const int MaxPendingJobs = 2;
    internal const int MaxRenderPixels = 12_000_000;

    internal WindowsPdfSpooler(PrinterSelection selection, Func<string, string?>? outputFileForCheck = null)
    {
        this.selection = selection;
        this.outputFileForCheck = outputFileForCheck;
        server = new PrintServer(selection.Server);
        try { queue = new PrintQueue(server, selection.Queue, PrintSystemDesiredAccess.UsePrinter); }
        catch { server.Dispose(); throw; }
    }

    public Task WaitForCapacityAsync(CancellationToken token)
    {
        while (pending.Count >= MaxPendingJobs)
        {
            token.ThrowIfCancellationRequested();
            using var jobs = queue.GetPrintJobInfoCollection();
            var active = new HashSet<int>();
            foreach (var job in jobs)
            {
                using (job)
                    if (!job.IsCompleted && !job.IsPrinted && !job.IsDeleted) active.Add(job.JobIdentifier);
            }
            pending.IntersectWith(active);
            if (pending.Count >= MaxPendingJobs && token.WaitHandle.WaitOne(500)) token.ThrowIfCancellationRequested();
        }
        return Task.CompletedTask;
    }

    public Task SubmitAsync(string pdfPath, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Keep the saved PDF open read-only until it has been fully spooled.
        using var file = File.OpenRead(pdfPath);
        using var input = file.AsRandomAccessStream();
        var pdf = PdfDocument.LoadFromStreamAsync(input).AsTask(token).GetAwaiter().GetResult();
        if (pdf.PageCount == 0) throw new InvalidDataException("This PDF has no pages to print.");
        var dc = Native.CreateDC("WINSPOOL", selection.FullName, null, selection.DevMode);
        if (dc == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not connect to the selected printer.");
        var started = false;
        Native.AbortProcedure abort = (_, _) => !token.IsCancellationRequested;
        try
        {
            Check(Native.SetAbortProc(dc, abort), token, "Prepare printing");
            var info = new Native.DocInfo { Size = Marshal.SizeOf<Native.DocInfo>(), Name = Path.GetFileName(pdfPath), Output = outputFileForCheck?.Invoke(pdfPath) };
            var jobId = Native.StartDoc(dc, ref info);
            Check(jobId, token, "Start print job");
            started = true;
            for (uint i = 0; i < pdf.PageCount; i++)
            {
                token.ThrowIfCancellationRequested();
                using var page = pdf.GetPage(i);
                PrintPage(dc, page, token);
            }
            token.ThrowIfCancellationRequested();
            Check(Native.EndDoc(dc), token, "Finish print job");
            started = false;
            pending.Add(jobId);
        }
        finally
        {
            if (started) Native.AbortDoc(dc);
            Native.DeleteDC(dc);
            GC.KeepAlive(abort);
        }
        return Task.CompletedTask;
    }

    static void PrintPage(IntPtr dc, PdfPage page, CancellationToken token)
    {
        var width = Native.GetDeviceCaps(dc, 8); // HORZRES: printable area, excluding hardware margins.
        var height = Native.GetDeviceCaps(dc, 10);
        var dpiX = Native.GetDeviceCaps(dc, 88);
        var dpiY = Native.GetDeviceCaps(dc, 90);
        if (width <= 0 || height <= 0 || dpiX <= 0 || dpiY <= 0) throw new IOException("The printer reported an invalid paper size.");
        // Fit in physical units, including printers whose horizontal and vertical resolutions differ.
        var scale = Math.Min(width / (double)dpiX / page.Size.Width, height / (double)dpiY / page.Size.Height);
        var printWidth = Math.Max(1, (int)Math.Floor(page.Size.Width * scale * dpiX));
        var printHeight = Math.Max(1, (int)Math.Floor(page.Size.Height * scale * dpiY));
        var renderScale = scale * Math.Min(300, Math.Min(dpiX, dpiY));
        renderScale = Math.Min(renderScale, Math.Sqrt(MaxRenderPixels / (page.Size.Width * page.Size.Height)));
        using var stream = new InMemoryRandomAccessStream();
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Floor(page.Size.Width * renderScale)),
            DestinationHeight = (uint)Math.Max(1, Math.Floor(page.Size.Height * renderScale)),
            BackgroundColor = new Windows.UI.Color { A = 255, R = 255, G = 255, B = 255 }
        };
        // Wait for a native render to finish before disposing its PDF/stream; then honour cancellation.
        page.RenderToStreamAsync(stream, options).AsTask().GetAwaiter().GetResult();
        token.ThrowIfCancellationRequested();
        stream.Seek(0);
        using var managed = stream.AsStreamForRead();
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = managed; bitmap.EndInit();
        BitmapSource source = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = checked(source.PixelWidth * 4);
        var pixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(pixels, stride, 0);
        var header = new Native.BitmapInfo
        {
            Size = 40, Width = source.PixelWidth, Height = -source.PixelHeight,
            Planes = 1, BitCount = 32, ImageSize = (uint)pixels.Length
        };
        Check(Native.StartPage(dc), token, "Start printed page");
        Native.SetStretchBltMode(dc, 4); // HALFTONE
        Native.SetBrushOrgEx(dc, 0, 0, IntPtr.Zero);
        var copied = Native.StretchDIBits(dc, (width - printWidth) / 2, (height - printHeight) / 2,
            printWidth, printHeight, 0, 0, source.PixelWidth, source.PixelHeight, pixels, ref header, 0, 0x00CC0020);
        if (copied == 0 || copied == -1) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not render this page to the printer.");
        Check(Native.EndPage(dc), token, "Finish printed page");
    }

    static void Check(int result, CancellationToken token, string operation)
    {
        if (result > 0) return;
        token.ThrowIfCancellationRequested();
        throw new Win32Exception(Marshal.GetLastWin32Error(), operation + " failed.");
    }
    public void Dispose() { queue.Dispose(); server.Dispose(); }

    static class Native
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DocInfo { public int Size; public string Name; public string? Output; public string? DataType; public uint Type; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapInfo
        {
            public uint Size; public int Width, Height; public ushort Planes, BitCount;
            public uint Compression, ImageSize; public int XPelsPerMeter, YPelsPerMeter; public uint ColoursUsed, ColoursImportant;
        }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal delegate bool AbortProcedure(IntPtr dc, int error);
        [DllImport("gdi32.dll", EntryPoint = "CreateDCW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateDC(string driver, string device, string? output, byte[] devMode);
        [DllImport("gdi32.dll", EntryPoint = "StartDocW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int StartDoc(IntPtr dc, ref DocInfo info);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern int EndDoc(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern int AbortDoc(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern int SetAbortProc(IntPtr dc, AbortProcedure procedure);
        [DllImport("gdi32.dll")] internal static extern int GetDeviceCaps(IntPtr dc, int index);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern int StartPage(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] internal static extern int EndPage(IntPtr dc);
        [DllImport("gdi32.dll")] internal static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] internal static extern bool SetBrushOrgEx(IntPtr dc, int x, int y, IntPtr previous);
        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern int StretchDIBits(IntPtr dc, int x, int y, int width, int height,
            int sourceX, int sourceY, int sourceWidth, int sourceHeight, byte[] bits, ref BitmapInfo info, uint usage, uint operation);
    }
}
