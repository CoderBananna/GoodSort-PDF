using System.IO;
using System.Windows;
using System.Text.Json;
using NewsletterCombiner.Core;

namespace NewsletterCombiner.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] is "--mode-smoke" or "--page-smoke" or "--print-smoke")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { if (e.Args[0] == "--page-smoke") await WindowsPageSmoke.RunAsync(e.Args[1]); else if (e.Args[0] == "--print-smoke") await WindowsPrintSmoke.RunAsync(e.Args[1]); else await WindowsModeSmoke.RunAsync(e.Args[1]); Shutdown(0); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(e.Args[1]);
                File.WriteAllText(Path.Combine(e.Args[1], "error.txt"), ex.ToString());
                Shutdown(1);
            }
            return;
        }
        // Also used by the Windows compression checks. The source is never modified.
        if (e.Args.Length == 3 && e.Args[0] == "--compress-pdf")
        {
            try
            {
                File.Copy(e.Args[1], e.Args[2], overwrite: false);
                var passes = new List<string>();
                var result = new PdfSizeOptimizer(new WpfRasterCodec()).Optimize(e.Args[2], status: passes.Add);
                File.WriteAllText(e.Args[2] + ".compression.json", JsonSerializer.Serialize(new { result.OriginalBytes, result.FinalBytes, result.OverTarget, Passes = passes }));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(e.Args[2] + ".error.txt", ex.ToString()); } catch { }
                Shutdown(1);
            }
            return;
        }
        if (e.Args.Length == 4 && e.Args[0] == "--office-export")
        {
            try { OfficeExporter.Export(e.Args[1], e.Args[2], e.Args[3]); Shutdown(0); }
            catch (Exception ex)
            {
                try { File.WriteAllText(e.Args[2] + ".error.txt", ex.Message); } catch { }
                Shutdown(1);
            }
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "GoodSort PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
}
