using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkyAPI.Desktop;

// Visual QA harness: renders every 1.1 screen in both themes, at 90/100/140% and in a narrow window, saves a PNG
// of each and reports layout defects found by walking the visual tree. Lives outside the product.
internal static class Qa {
    private static readonly List<string> Findings = new();
    private static int hintsChecked;

    [STAThread]
    private static void Main() {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var outDir = Environment.GetEnvironmentVariable("QA_OUT") ?? "qa";
        Directory.CreateDirectory(outDir);
        var screens = new (string File, string Method, object[] Args)[] {
            ("inicial", "Home", Array.Empty<object>()),
            ("menu-licencas", "LicenseMenu", Array.Empty<object>()),
            ("menu-relatorios", "ReportMenu", Array.Empty<object>()),
            ("menu-grupos", "GroupMenu", Array.Empty<object>()),
            ("licencas", "Advanced", new object[] { "licenses", "", "", "" }),
            ("upgrade", "Advanced", new object[] { "upgrade", "", "", "" }),
            ("grupos", "Advanced", new object[] { "groups", "", "", "" }),
            ("substituicao", "Advanced", new object[] { "replace", "", "", "" }),
            ("recebidas", "Advanced", new object[] { "received", "", "", "" }),
            ("enviadas", "Advanced", new object[] { "sent", "", "", "" }),
            ("gerenciar-grupos", "Advanced", new object[] { "groupedit", "", "", "" }),
            ("processos", "Processes", Array.Empty<object>()),
        };
        int shots = 0;
        foreach (var dark in new[] { false, true })
        foreach (var scale in new[] { 0.90, 1.00, 1.40 })
        foreach (var width in new[] { 1320.0, 760.0 }) {
            if (width < 1000 && scale != 1.00) continue;   // janela estreita conferida na escala padrao
            Theme.Apply(dark);
            Theme.SetScale(scale);
            var window = NewWindow(width);
            window.Show();
            foreach (var screen in screens) {
                Navigate(window, screen.Method, screen.Args);
                var name = string.Join("_", screen.File, dark ? "escuro" : "claro",
                    ((int)Math.Round(scale * 100)).ToString(CultureInfo.InvariantCulture),
                    width < 1000 ? "estreito" : "largo") + ".png";
                Save(window, Path.Combine(outDir, name));
                shots++;
                Inspect(window, name);
            }
            window.Close();
        }
        // Demonstracao: os modulos novos precisam avisar que nao tem simulacao.
        Theme.Apply(false);
        Theme.SetScale(1.00);
        var demoWindow = NewWindow(1320);
        typeof(MainWindow).GetField("demo", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(demoWindow, true);
        demoWindow.Show();
        var demoScreens = new (string File, string Method, object[] Args)[] {
            ("menu-licencas", "LicenseMenu", Array.Empty<object>()),
            ("grupos", "Advanced", new object[] { "groups", "", "", "" }),
            ("gerenciar-grupos", "Advanced", new object[] { "groupedit", "", "", "" }),
        };
        foreach (var screen in demoScreens) {
            Navigate(demoWindow, screen.Method, screen.Args);
            var name = "demo_" + screen.File + ".png";
            Save(demoWindow, Path.Combine(outDir, name));
            shots++;
            var texts = Find<TextBlock>(demoWindow).Select(t => t.Text ?? "").ToArray();
            if (!texts.Any(t => t.Contains("Nao e possivel demonstrar") || t.Contains("é possível demonstrar")))
                Findings.Add(name + ": aviso de modulo sem demonstracao nao apareceu");
            if (texts.Any(t => t.Contains("marcados como simulação")))
                Findings.Add(name + ": ainda promete simulacao neste modulo");
            Inspect(demoWindow, name);
        }
        demoWindow.Close();

        var report = new StringBuilder();
        report.AppendLine("Imagens geradas: " + shots);
        report.AppendLine("Campos com exemplo conferidos: " + hintsChecked);
        report.AppendLine("Achados: " + Findings.Distinct().Count());
        foreach (var finding in Findings.Distinct()) report.AppendLine("  - " + finding);
        File.WriteAllText(Path.Combine(outDir, "relatorio.txt"), report.ToString(), new UTF8Encoding(false));
        Console.WriteLine(report.ToString());
        app.Shutdown();
    }

    private static MainWindow NewWindow(double width) => new MainWindow {
        Width = width, Height = 900, WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -4000, Top = 0, ShowInTaskbar = false
    };

    private static void Navigate(Window window, string method, object[] args) {
        typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args);
        Pump(window);
    }

    private static void Pump(Window window) {
        for (int i = 0; i < 3; i++) {
            window.UpdateLayout();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
        window.UpdateLayout();
    }

    private static void Save(Window window, string path) {
        int w = (int)Math.Ceiling(window.ActualWidth), h = (int)Math.Ceiling(window.ActualHeight);
        if (w <= 0 || h <= 0) return;
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) {
            context.DrawRectangle(Theme.Bg, null, new Rect(0, 0, w, h));
            context.DrawRectangle(new VisualBrush(window) {
                Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top
            }, null, new Rect(0, 0, w, h));
        }
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject {
        if (root is T hit) yield return hit;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static bool Inside<T>(DependencyObject node) where T : DependencyObject {
        for (var parent = node; parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is T) return true;
        return false;
    }

    private static void Inspect(Window window, string shot) {
        foreach (var text in Find<TextBlock>(window)) {
            if (text.ActualWidth <= 0 || string.IsNullOrEmpty(text.Text)) continue;
            if (text.TextWrapping != TextWrapping.NoWrap) continue;
            var natural = new FormattedText(text.Text, CultureInfo.CurrentCulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            if (natural.Width > text.ActualWidth + 1.0)
                Findings.Add(shot + ": texto cortado \"" + Shorten(text.Text) + "\" (precisa " +
                    Math.Round(natural.Width) + "px, tem " + Math.Round(text.ActualWidth) + "px)");
        }
        // O exemplo dentro do campo tem de comecar exatamente onde o texto digitado comeca.
        foreach (var box in Find<TextBox>(window)) {
            // Campo de varias linhas com alinhamento central poe o cursor no meio da caixa: foi o que fez o
            // exemplo parecer desalinhado mesmo depois de alinhado horizontalmente.
            if (box.AcceptsReturn && box.VerticalContentAlignment != VerticalAlignment.Top)
                Findings.Add(shot + ": campo de varias linhas com cursor fora da primeira linha");
            // Exemplo por sobreposicao nao enxerga o padding do campo: so vale o do template.
            if (Ui.GetHint(box).Length == 0) {
                var over = VisualTreeHelper.GetParent(box) is Grid cell
                    ? cell.Children.OfType<TextBlock>().Any(t => !t.IsHitTestVisible && t.Text.Length > 0) : false;
                if (over) Findings.Add(shot + ": campo com exemplo sobreposto por fora, em vez de Ui.Hint");
                continue;
            }
            hintsChecked++;
            var ghost = box.Template?.FindName("hint", box) as FrameworkElement;
            var host = box.Template?.FindName("PART_ContentHost", box) as FrameworkElement;
            if (ghost == null || host == null) { Findings.Add(shot + ": campo com exemplo sem a camada no template"); continue; }
            if (ghost.ActualWidth <= 0 || host.ActualWidth <= 0) continue;
            if (box.Text.Length != 0 || ghost is not TextBlock hint) continue;
            // Medir o cursor real: a origem do ScrollViewer ignora o padding e a margem do TextBoxView.
            var caret = box.GetRectFromCharacterIndex(0);
            var first = hint.ContentStart.GetCharacterRect(System.Windows.Documents.LogicalDirection.Forward);
            var origin = hint.TranslatePoint(first.TopLeft, box);
            if (caret.IsEmpty || Math.Abs(caret.X - origin.X) > 0.75 || Math.Abs(caret.Y - origin.Y) > 0.75)
                Findings.Add(shot + ": exemplo desalinhado do cursor real (dx " +
                    Math.Round(origin.X - caret.X, 2) + ", dy " + Math.Round(origin.Y - caret.Y, 2) + ")");
        }
        foreach (var viewer in Find<ScrollViewer>(window)) {
            if (Inside<DataGrid>(viewer)) continue;   // tabelas podem rolar na horizontal por natureza
            if (viewer.ScrollableWidth > 1.0)
                Findings.Add(shot + ": rolagem horizontal necessaria (" +
                    Math.Round(viewer.ScrollableWidth) + "px alem da largura)");
        }
    }

    private static string Shorten(string value) => value.Length <= 46 ? value : value.Substring(0, 45) + "…";
}
