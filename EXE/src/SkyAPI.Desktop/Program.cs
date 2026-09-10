using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.IO.Path;
using Microsoft.Win32;
using SkyAPI.Core;

namespace SkyAPI.Desktop;

public static class Program {
    [STAThread]
    public static void Main() {
        var app = new Application();
        app.DispatcherUnhandledException += (s, e) => {
            MessageBox.Show(
                "O aplicativo encontrou um erro inesperado. Se um lote estava em execução, confira os resultados no painel antes de repetir.\n\nFeche e abra o SkyAPI novamente.",
                "SkyAPI", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            app.Shutdown(1);
        };
        Theme.Load();
        app.Run(new MainWindow());
    }
}

/// <summary>Ajustes da janela que dependem do Windows (barra de título escura).</summary>
internal static class Native {
    private const int UseImmersiveDarkMode = 20, UseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    /// <summary>Acompanha o tema na barra de título. Em versões antigas do Windows simplesmente não tem efeito.</summary>
    public static void TitleBar(Window window, bool dark) {
        try {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeLegacy, ref value, sizeof(int));
        } catch (DllNotFoundException) {
            // Sem dwmapi.dll a janela continua funcionando com a barra padrão.
        } catch (EntryPointNotFoundException) {
        }
    }
}

public sealed partial class MainWindow : Window {
    private const string Version = "1.1.12";

    private readonly ApiClient api = new();
    private readonly List<ApiResult> results = new();
    private readonly Grid root = new();

    // Reconstruídos a cada troca de tema ou de tamanho de texto.
    private Grid shell = new();
    private StackPanel page = new();
    private TextBlock title = new(), subtitle = new();
    private Button connectButton = new();
    private Panel headerActions = new StackPanel();
    private Grid header = new();
    private TextBlock detail = new();
    private readonly Dictionary<string, Button> navButtons = new();

    // Cartão de conexão do rodapé lateral, atualizado sem remontar a barra.
    private Border statusCard = new();
    private Ellipse statusDot = new();
    private TextBlock statusHeading = new(), statusNote = new();
    private bool headerPlaced;

    private Plan? plan;
    private Operation operation;
    private bool demo, busy, stopRequested, compact;
    private Action? pageCleanup;
    private System.Windows.Threading.DispatcherTimer? processTimer;
    private string inputText = "", status = "disabled", sharedPassword = "";
    private string? reportPath;

    private string screen = "home";
    private Action render;

    private readonly (Operation op, string title, string description, string glyph, bool destructive)[] cards = {
        (Operation.DeleteAccounts,  "Excluir contas",      "Remova as contas de e-mail selecionadas.",        "\uE74D", true),
        (Operation.RestoreAccounts, "Recuperar contas",    "Solicite a restauração das contas excluídas.",    "\uE777", false),
        (Operation.AccountStatus,   "Alterar status",      "Desabilite, bloqueie ou reative contas.",         "\uE713", false),
        (Operation.PasswordSame,    "Gerenciar senhas",    "Altere senhas ou exija a troca no login.",        "\uE72E", false),
        (Operation.RenameAccounts,  "Renomear contas",     "Atualize o endereço das caixas postais.",         "\uE8AC", false),
        (Operation.Attributes,      "Atualizar atributos", "Edite nome, cargo, telefones e outros campos.",   "\uE77B", false),
        (Operation.DeleteGroups,    "Excluir grupos",      "Remova permanentemente grupos de e-mail.",        "\uE716", true),
        (Operation.DeleteDns,       "Excluir zonas DNS",   "Remova permanentemente as zonas dos domínios.",   "\uE774", true)
    };

    public MainWindow() {
        render = Home;
        api.RateLimitWaiting+=until=>Dispatcher.BeginInvoke(new Action(()=>{
            if(until.HasValue)subtitle.Text="Aguardando o limite da API renovar às "+until.Value.ToLocalTime().ToString("HH:mm:ss")+". A operação continuará automaticamente.";
            else if(subtitle.Text.StartsWith("Aguardando o limite da API"))subtitle.Text="Limite liberado. Continuando operação…";
        }));
        Title = "SkyAPI | Skynova";
        Icon = LoadIcon();
        Width = Math.Min(1240, SystemParameters.WorkArea.Width);
        Height = Math.Min(880, SystemParameters.WorkArea.Height);
        MinWidth = 720;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        Content = root;

        SourceInitialized += (s, e) => Native.TitleBar(this, Theme.Dark);
        SizeChanged += (s, e) => Reflow();
        Closing += OnClosing;
        Closed += (s, e) => api.Dispose();

        Rebuild();
        Home();
    }

    /// <summary>
    /// Ícone da janela e da barra de tarefas. O .ico é multi-resolução; escolhemos a
    /// moldura de 32 px, que o Windows reduz bem para a barra de título.
    /// </summary>
    private static ImageSource? LoadIcon() {
        try {
            using var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("SkyApiIcon");
            if (resource == null) return null;
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            buffer.Position = 0;
            var decoder = new IconBitmapDecoder(buffer, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).First();
            frame.Freeze();
            return frame;
        } catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or InvalidOperationException) {
            return null;
        }
    }

    // =====================================================================
    // Estrutura da janela
    // =====================================================================

    /// <summary>Remonta a moldura inteira com a paleta e a escala atuais.</summary>
    private void Rebuild() {
        Ui.Install(this);
        Background = Theme.Bg;
        Foreground = Theme.Ink;
        Native.TitleBar(this, Theme.Dark);

        root.Children.Clear();
        navButtons.Clear();
        headerPlaced = false;

        shell = new Grid { LayoutTransform = new ScaleTransform(Theme.Scale, Theme.Scale) };
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(shell);

        var side = BuildSidebar();
        Grid.SetColumn(side, 0);
        shell.Children.Add(side);

        var main = BuildMain();
        Grid.SetColumn(main, 1);
        shell.Children.Add(main);

        RefreshConnection();
        Reflow();
    }

    private Border BuildSidebar() {
        var dock = new DockPanel { LastChildFill = true };

        // --- marca -------------------------------------------------------
        var brand = new StackPanel { Margin = new Thickness(20, 24, 20, 16) };
        brand.Children.Add(new Image {
            Source = Logo.Full(Theme.Dark),
            Height = 40,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            SnapsToDevicePixels = true
        });
        brand.Children.Add(Txt("SkyAPI", 25, Theme.Heading, FontWeights.SemiBold, top: 18));
        brand.Children.Add(Txt("ASSISTENTE DE OPERAÇÕES", 10, Theme.Muted, FontWeights.SemiBold, top: 3));
        DockPanel.SetDock(brand, Dock.Top);
        dock.Children.Add(brand);

        // --- rodapé: aparência, conexão e versão -------------------------
        var foot = new StackPanel { Margin = new Thickness(14, 10, 14, 16) };
        foot.Children.Add(Rule(new Thickness(4, 0, 4, 14)));
        foot.Children.Add(Caption("TAMANHO DO TEXTO", new Thickness(6, 0, 0, 8)));
        foot.Children.Add(BuildScaleRow());
        foot.Children.Add(BuildThemeToggle());
        foot.Children.Add(Rule(new Thickness(4, 16, 4, 14)));
        foot.Children.Add(BuildStatusCard());
        foot.Children.Add(Txt("SkyAPI " + Version + " · Windows", 11, Theme.Muted, top: 12, left: 6));
        DockPanel.SetDock(foot, Dock.Bottom);
        dock.Children.Add(foot);

        // --- navegação ---------------------------------------------------
        var nav = new StackPanel { Margin = new Thickness(12, 6, 12, 0) };
        nav.Children.Add(Nav("home", "\uE80F", "Operações em lote", Home));
        nav.Children.Add(Nav("licenses", "\uE8D7", "Licenças", LicenseMenu));
        nav.Children.Add(Nav("reports", "\uE9F9", "Relatórios", ReportMenu));
        nav.Children.Add(Nav("groups", "\uE716", "Grupos", GroupMenu));
        nav.Children.Add(Nav("replace", "\uE713", "Ferramentas", () => Advanced("replace")));
        nav.Children.Add(Nav("jobs", "\uE9F5", "Processos", Processes));
        nav.Children.Add(Nav("auth", "\uE72E", "Conexão com a API", Auth));
        nav.Children.Add(Nav("help", "\uE897", "Ajuda e formatos", Help));
        dock.Children.Add(new ScrollViewer {
            Content = nav,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

        return new Border {
            Background = Theme.Side,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = dock
        };
    }

    private Grid BuildScaleRow() {
        var row = new Grid { Margin = new Thickness(4, 0, 4, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var smaller = StepButton("\uE738", "Diminuir o tamanho do texto", -1);
        var bigger = StepButton("\uE710", "Aumentar o tamanho do texto", +1);
        var label = Txt(Theme.ScaleLabel, 13, Theme.Ink, FontWeights.SemiBold);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        label.ToolTip = "Tamanho atual do texto";

        Grid.SetColumn(smaller, 0);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(bigger, 2);
        row.Children.Add(smaller);
        row.Children.Add(label);
        row.Children.Add(bigger);
        return row;
    }

    private Button StepButton(string glyph, string tip, int direction) {
        var button = new Button {
            Style = (Style)FindResource(Ui.QuietButton),
            Width = 38,
            Height = 32,
            Padding = new Thickness(0),
            ToolTip = tip,
            IsEnabled = !busy && Theme.CanStepScale(direction),
            Content = new TextBlock {
                Text = glyph,
                FontFamily = new FontFamily(Ui.Icons),
                FontSize = 11,
                Foreground = Theme.Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += (s, e) => {
            if (busy || !Theme.StepScale(direction)) return;
            Theme.Save();
            Rebuild();
            render();
        };
        return button;
    }

    private Button BuildThemeToggle() {
        bool dark = Theme.Dark;
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var glyph = new TextBlock {
            Text = dark ? "\uE706" : "\uE708",
            FontFamily = new FontFamily(Ui.Icons),
            FontSize = 15,
            Foreground = dark ? Theme.Cyan : Theme.Accent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        var label = Txt(dark ? "Modo claro" : "Modo escuro", 13, Theme.Ink);
        label.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(label, 1);
        content.Children.Add(glyph);
        content.Children.Add(label);

        var button = new Button {
            Style = (Style)FindResource(Ui.QuietButton),
            Content = content,
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(4, 0, 4, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !busy,
            ToolTip = dark ? "Voltar ao tema claro" : "Usar o tema escuro"
        };
        button.Click += (s, e) => {
            if (busy) return;
            Theme.Apply(!Theme.Dark);
            Theme.Save();
            Rebuild();
            render();
        };
        return button;
    }

    /// <summary>Cartão do rodapé lateral: diz em uma olhada como está a conexão.</summary>
    private Border BuildStatusCard() {
        var (heading, note, tint, dot) = ConnectionState();

        statusDot = new Ellipse {
            Width = 9, Height = 9, Fill = dot,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        statusHeading = Txt(heading, 12.5, Theme.Ink, FontWeights.SemiBold);
        statusHeading.VerticalAlignment = VerticalAlignment.Center;
        statusNote = Txt(note, 11.5, Theme.Muted, top: 5);

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(statusDot);
        line.Children.Add(statusHeading);
        var stack = new StackPanel();
        stack.Children.Add(line);
        stack.Children.Add(statusNote);

        return statusCard = new Border {
            Background = tint,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 11, 12, 11),
            Margin = new Thickness(4, 0, 4, 0),
            Child = stack
        };
    }

    private (string heading, string note, Brush tint, Brush dot) ConnectionState() {
        if (demo)
            return ("Modo demonstração", "Nenhuma solicitação é enviada à API.", Theme.InfoSoft, Theme.Info);
        if (api.HasToken)
            return ("Conectado à API", "Token ativo nesta sessão.", Theme.OkSoft, Theme.Ok);
        return ("Não conectado", "Configure o acesso para executar.", Theme.WarnSoft, Theme.Warn);
    }

    private DockPanel BuildMain() {
        var main = new DockPanel { Margin = new Thickness(32, 26, 32, 14) };

        header = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        title = new TextBlock { FontSize = 27, FontWeight = FontWeights.SemiBold, Foreground = Theme.Heading, TextWrapping = TextWrapping.Wrap };
        subtitle = new TextBlock { FontSize = 13.5, Foreground = Theme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 16, 0) };
        var headings = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headings.Children.Add(title);
        headings.Children.Add(subtitle);
        Grid.SetRow(headings, 0);
        Grid.SetColumn(headings, 0);
        header.Children.Add(headings);

        connectButton = new Button { Padding = new Thickness(15, 9, 15, 9) };
        connectButton.Click += (s, e) => {
            if (busy) return;
            // Em demonstração o botão promete sair dela: precisa sair de fato, não só navegar.
            if (demo) {
                demo = false;
                api.ClearToken();
                RefreshConnection();
            }
            Auth();
        };

        headerActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        headerActions.Children.Add(new Border { Name = "pill" });   // trocado por RefreshConnection
        headerActions.Children.Add(connectButton);
        header.Children.Add(headerActions);

        DockPanel.SetDock(header, Dock.Top);
        main.Children.Add(header);

        var foot = Txt("Comunicação direta com a API Skymail · Credenciais apenas nesta sessão", 11, Theme.Muted, top: 16);
        DockPanel.SetDock(foot, Dock.Bottom);
        main.Children.Add(foot);

        page = new StackPanel();
        main.Children.Add(new ScrollViewer {
            Content = page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 6, 0)
        });
        return main;
    }

    // =====================================================================
    // Responsividade
    // =====================================================================

    /// <summary>Largura útil já descontada a ampliação do texto.</summary>
    private double Logical => Math.Max(360, (root.ActualWidth > 0 ? root.ActualWidth : Width) / Theme.Scale);

    private void Reflow() {
        if (shell.ColumnDefinitions.Count < 2) return;
        double width = Logical;
        shell.ColumnDefinitions[0].Width = new GridLength(width >= 1080 ? 244 : width >= 900 ? 214 : 192);

        bool narrow = width < 880;
        if (headerPlaced && narrow == compact) return;
        compact = narrow;
        headerPlaced = true;

        if (compact) {
            Grid.SetRow(headerActions, 1);
            Grid.SetColumn(headerActions, 0);
            Grid.SetColumnSpan(headerActions, 2);
            headerActions.HorizontalAlignment = HorizontalAlignment.Left;
            headerActions.Margin = new Thickness(0, 14, 0, 0);
        } else {
            Grid.SetRow(headerActions, 0);
            Grid.SetColumn(headerActions, 1);
            Grid.SetColumnSpan(headerActions, 1);
            headerActions.HorizontalAlignment = HorizontalAlignment.Right;
            headerActions.Margin = new Thickness(12, 4, 0, 0);
        }
    }

    // =====================================================================
    // Peças reutilizáveis
    // =====================================================================

    private static TextBlock Txt(string text, double size = 14, Brush? color = null, FontWeight? weight = null, double left = 0, double top = 0) => new() {
        Text = text,
        FontSize = size,
        Foreground = color ?? Theme.Ink,
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(left, top, 0, 0)
    };

    private static TextBlock Caption(string text, Thickness margin) => new() {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = Theme.Muted,
        Margin = margin
    };

    private static Border Rule(Thickness margin) => new() {
        Height = 1,
        Background = Theme.LineSoft,
        Margin = margin
    };

    private static TextBlock Glyph(string glyph, double size, Brush color) => new() {
        Text = glyph,
        FontFamily = new FontFamily(Ui.Icons),
        FontSize = size,
        Foreground = color,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private Button Btn(string label, Action action, bool primary = false) {
        var button = new Button {
            Content = label,
            Margin = new Thickness(0, 0, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Left  // não estica dentro de pilhas verticais
        };
        if (primary) button.Style = (Style)FindResource(Ui.PrimaryButton);
        button.Click += (s, e) => action();
        return button;
    }

    private Button Nav(string key, string glyph, string label, Action action) {
        bool active = screen == key;
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new TextBlock {
            Text = glyph,
            FontFamily = new FontFamily(Ui.Icons),
            FontSize = 16,
            Foreground = active ? Theme.Accent : Theme.Muted,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        var text = new TextBlock {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(text, 1);
        content.Children.Add(icon);
        content.Children.Add(text);

        var button = new Button {
            Style = (Style)FindResource(active ? Ui.NavButtonActive : Ui.NavButton),
            Content = content,
            Margin = new Thickness(0, 0, 0, 4)
        };
        button.Click += (s, e) => { if (!busy) action(); };
        navButtons[key] = button;
        return button;
    }

    private static Border Panel(UIElement child) => new() {
        Background = Theme.Surface,
        BorderBrush = Theme.Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(24),
        Child = child,
        Margin = new Thickness(0, 0, 0, 16)
    };

    /// <summary>Faixa colorida com marcador, usada para avisos e para o estado da conexão.</summary>
    private static Border Banner(string glyph, string heading, string note, Brush tint, Brush accent, UIElement? action = null) {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var badge = new Border {
            Width = 34, Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = accent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
            Child = Glyph(glyph, 15, Theme.Surface)
        };
        Grid.SetColumn(badge, 0);
        grid.Children.Add(badge);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(Txt(heading, 15, Theme.Ink, FontWeights.SemiBold));
        if (note.Length > 0) texts.Children.Add(Txt(note, 12.5, Theme.Muted, top: 4));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        if (action != null) {
            if (action is FrameworkElement element) {
                element.VerticalAlignment = VerticalAlignment.Center;
                element.Margin = new Thickness(14, 0, 0, 0);
            }
            Grid.SetColumn(action, 2);
            grid.Children.Add(action);
        }

        return new Border {
            Background = tint,
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 18),
            Child = grid
        };
    }

    /// <summary>demoNote troca o aviso de demonstração pelo aviso de módulo que não pode ser simulado.</summary>
    private void Clear(string key, string name, string desc, string? demoNote = null) {
        screen = key;
        var cleanup = pageCleanup; pageCleanup = null; cleanup?.Invoke();
        processTimer?.Stop(); processTimer = null;
        page.Children.Clear();
        title.Text = name;
        subtitle.Text = desc;
        foreach (var pair in navButtons) {
            bool active = pair.Key == key;
            pair.Value.Style = (Style)FindResource(active ? Ui.NavButtonActive : Ui.NavButton);
            if (pair.Value.Content is Grid grid && grid.Children[0] is TextBlock icon)
                icon.Foreground = active ? Theme.Accent : Theme.Muted;
        }
        RefreshConnection();   // etiqueta, cartao lateral e botao do topo acompanham a tela atual
        UpdateJobsBadge();     // a contagem de processos sobrevive a troca de tela e de tema
        if (demo && key != "auth")
            page.Children.Add(demoNote == null
                ? Banner("\uE946", "Modo demonstração",
                    "Nenhuma solicitação será enviada à API. Os relatórios saem marcados como simulação.",
                    Theme.InfoSoft, Theme.Info)
                : Banner("\uE7BA", "Não é possível demonstrar este módulo", demoNote, Theme.WarnSoft, Theme.Warn));
    }

    /// <summary>Atualiza a pílula do topo, o cartão lateral e o rótulo do botão de conexão.</summary>
    private void RefreshConnection() {
        var (heading, _, tint, dot) = ConnectionState();
        connectButton.Content = demo ? "Sair da demonstração" : api.HasToken ? "Gerenciar conexão" : "Conectar à API";
        connectButton.Style = (Style)FindResource(demo || api.HasToken ? typeof(Button) : (object)Ui.PrimaryButton);
        // Na própria tela de conexão o botão do topo seria repetido: a faixa já traz a ação.
        connectButton.Visibility = screen == "auth" ? Visibility.Collapsed : Visibility.Visible;

        var pill = new StackPanel { Orientation = Orientation.Horizontal };
        pill.Children.Add(new Ellipse {
            Width = 9, Height = 9, Fill = dot,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        var pillText = Txt(heading, 12.5, Theme.Ink, FontWeights.SemiBold);
        pillText.VerticalAlignment = VerticalAlignment.Center;
        pill.Children.Add(pillText);

        if (headerActions.Children.Count > 0) headerActions.Children.RemoveAt(0);
        headerActions.Children.Insert(0, new Border {
            Background = tint,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(13, 7, 15, 7),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = pill
        });

        // O cartão do rodapé lateral mostra o mesmo estado, sem remontar a barra.
        var (_, note, _, _) = ConnectionState();
        statusCard.Background = tint;
        statusDot.Fill = dot;
        statusHeading.Text = heading;
        statusNote.Text = note;
    }

    // =====================================================================
    // Indicador de etapas
    // =====================================================================

    private void Steps(int current) {
        string[] labels = { "Operação", "Dados", "Conferência", "Resultado" };
        var grid = new Grid();
        for (int i = 0; i < 7; i++)
            grid.ColumnDefinitions.Add(i % 2 == 0
                ? new ColumnDefinition { Width = GridLength.Auto }
                : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 14 });

        var texts = new TextBlock[4];
        for (int i = 0; i < 4; i++) {
            bool done = i < current, now = i == current;
            var item = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var badge = new Border {
                Width = 26, Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = done || now ? Theme.Accent : Theme.Field,
                BorderBrush = done || now ? Theme.Accent : Theme.Line,
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = done
                    ? Glyph("\uE73E", 11, Theme.AccentInk)
                    : new TextBlock {
                        Text = (i + 1).ToString(),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = now ? Theme.AccentInk : Theme.Muted,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
            };
            item.Children.Add(badge);

            texts[i] = new TextBlock {
                Text = labels[i],
                FontSize = 13,
                FontWeight = now ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = now ? Theme.Ink : Theme.Muted,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(9, 0, 0, 0)
            };
            item.Children.Add(texts[i]);

            Grid.SetColumn(item, i * 2);
            grid.Children.Add(item);

            if (i < 3) {
                var link = new Rectangle {
                    Height = 2,
                    Fill = i < current ? Theme.Accent : Theme.LineSoft,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 12, 0)
                };
                Grid.SetColumn(link, i * 2 + 1);
                grid.Children.Add(link);
            }
        }

        // Em larguras pequenas sobra só o rótulo da etapa atual, sem quebrar a linha.
        grid.SizeChanged += (s, e) => {
            bool tight = grid.ActualWidth < 520;
            for (int i = 0; i < 4; i++)
                texts[i].Visibility = !tight || i == current ? Visibility.Visible : Visibility.Collapsed;
        };

        page.Children.Add(new Border {
            Background = Theme.Surface,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(20, 15, 20, 15),
            Margin = new Thickness(0, 0, 0, 18),
            Child = grid
        });
    }

    private string OpName() => operation switch {
        Operation.PasswordSame or Operation.PasswordDifferent or Operation.ForcePasswordChange => "Gerenciar senhas",
        _ => cards.First(c => c.op == operation).title
    };

    // =====================================================================
    // Telas
    // =====================================================================

    private void Home() {
        if (busy) return;
        render = Home;
        plan = null;
        inputText = "";
        sharedPassword = "";
        Clear("home", "Operações em lote", "Escolha o que deseja fazer. Você poderá conferir tudo antes de executar.");
        Steps(0);

        // UniformGrid em vez de WrapPanel: as colunas dividem a largura exata disponível,
        // então a barra de rolagem que aparece não empurra um cartão para a linha seguinte.
        var wrap = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -14, 0) };
        page.Children.Add(wrap);
        var additions = new[] {
            ("licenses", "Alterar licenças", "Troque produtos com conferência por conta.", "\uE8D7"),
            ("upgrade", "Analisar upgrade", "Encontre caixas próximas do limite.", "\uE9D9"),
            ("groups", "Criar grupos em lote", "Importe membros, escritores e moderadores.", "\uE716"),
            ("groupedit", "Gerenciar grupos", "Inclua ou remova membros de vários grupos.", "\uE748"),
            ("replace", "Substituir colaborador", "Renomeie a conta e escolha se mantém o apelido.", "\uE713"),
            ("received", "Mensagens recebidas", "Consulte remetentes, domínios e períodos.", "\uE715"),
            ("sent", "Mensagens enviadas", "Consulte destinatários, domínios e períodos.", "\uE724")
        };
        foreach (var (kind, name, description, glyph) in additions)
            wrap.Children.Add(OperationCard(name, description, glyph, () => Advanced(kind)));
        foreach (var card in cards) {
            var button = new Button {
                Style = (Style)FindResource(Ui.CardButton),
                Margin = new Thickness(0, 0, 14, 14),
                Height = 158
            };
            button.Click += (s, e) => { operation = card.op; Input(); };

            var content = new StackPanel();
            content.Children.Add(new Border {
                Width = 42, Height = 42,
                CornerRadius = new CornerRadius(11),
                Background = card.destructive ? Theme.DangerSoft : Theme.AccentSoft,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = Glyph(card.glyph, 19, card.destructive ? Theme.Danger : Theme.Cyan)
            });
            content.Children.Add(Txt(card.title, 15.5, Theme.Ink, FontWeights.SemiBold, top: 16));
            content.Children.Add(Txt(card.description, 12.5, Theme.Muted, top: 6));
            button.Content = content;
            wrap.Children.Add(button);
        }

        void Resize() {
            double width = wrap.ActualWidth > 0 ? wrap.ActualWidth : page.ActualWidth;
            if (width <= 0) return;
            int columns = width >= 860 ? 3 : width >= 570 ? 2 : 1;
            if (wrap.Columns != columns) wrap.Columns = columns;
            double height = columns == 1 ? 142 : 158;
            foreach (Button b in wrap.Children) b.Height = height;
        }
        wrap.SizeChanged += (s, e) => Resize();
        Resize();

        page.Children.Add(Txt("As ações são executadas uma a uma, com resultado individual e relatório local.", 12, Theme.Muted, top: 4));

        if (!api.HasToken && !demo) {
            var row = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) };
            row.Children.Add(Btn("Conectar à API", Auth, true));
            row.Children.Add(Btn("Experimentar demonstração", () => {
                demo = true;
                api.ClearToken();
                RefreshConnection();
                Home();
            }));
            page.Children.Add(row);
        }
    }

    private void Auth() {
        if (busy) return;
        render = Auth;
        Clear("auth", "Conexão com a API", "Use o acesso administrativo do seu painel Skymail.");

        // --- estado atual, bem visível -----------------------------------
        if (api.HasToken) {
            page.Children.Add(Banner("\uE73E", "Conectado à API Skymail",
                "Token ativo nesta sessão. As permissões continuam sendo verificadas pela API a cada operação.",
                Theme.OkSoft, Theme.Ok,
                Btn("Desconectar", () => {
                    if (!ConfirmDisconnect()) return;
                    api.ClearToken(); demo = false; RefreshConnection(); Auth();
                })));
        } else if (demo) {
            page.Children.Add(Banner("\uE946", "Modo demonstração ativo",
                "Nenhuma solicitação é enviada à API. Conecte-se abaixo para executar operações reais.",
                Theme.InfoSoft, Theme.Info,
                Btn("Sair da demonstração", () => { demo = false; RefreshConnection(); Auth(); })));
        } else {
            page.Children.Add(Banner("\uE7BA", "Não conectado",
                "Informe suas credenciais abaixo para executar operações reais nas contas.",
                Theme.WarnSoft, Theme.Warn));
        }

        // --- formulário ---------------------------------------------------
        var p = new StackPanel { MaxWidth = 660, HorizontalAlignment = HorizontalAlignment.Left };
        p.Children.Add(Txt(api.HasToken ? "Trocar de acesso" : "Gerar acesso", 16, Theme.Ink, FontWeights.SemiBold));
        p.Children.Add(Txt("O token vale apenas para esta sessão do aplicativo.", 12.5, Theme.Muted, top: 5));

        var tabs = new ComboBox {
            ItemsSource = new[] { "Gerar token com usuário, senha e chave privada", "Usar um token JWT existente" },
            SelectedIndex = 0,
            Margin = new Thickness(0, 16, 0, 4)
        };
        p.Children.Add(tabs);

        var user = new TextBox();
        var password = new PasswordBox();
        var secret = new PasswordBox();
        var token = new PasswordBox();
        var fields = new StackPanel();
        p.Children.Add(fields);

        void Form() {
            fields.Children.Clear();
            if (tabs.SelectedIndex == 0) {
                Field(fields, "Usuário do painel (e-mail)", user);
                Field(fields, "Senha do painel", password);
                Field(fields, "Chave privada da organização", secret);
                fields.Children.Add(SecretHelp());
            } else {
                Field(fields, "Token JWT", token);
                fields.Children.Add(Txt("Cole o token completo. A validação local confere o formato; a API verifica as permissões durante a operação.", 12, Theme.Muted, top: 10));
            }
        }
        tabs.SelectionChanged += (s, e) => Form();
        Form();

        var message = Txt("", 13, Theme.Danger, top: 14);
        p.Children.Add(message);

        var connect = Btn("Conectar", () => { }, true);
        connect.Margin = new Thickness(0, 18, 0, 0);
        connect.Click += async (s, e) => {
            connect.IsEnabled = false;
            tabs.IsEnabled = false;
            fields.IsEnabled = false;
            if (!ConfirmDisconnect()) return;
            connectButton.IsEnabled = false;
            busy = true;
            message.Text = "Conectando…";
            message.Foreground = Theme.Muted;
            api.ClearToken();
            RefreshConnection();
            try {
                if (tabs.SelectedIndex == 0) await api.LoginAsync(user.Text, password.Password, secret.Password);
                else api.SetToken(token.Password);
                demo = false;
                password.Clear();
                secret.Clear();
                token.Clear();
                RefreshConnection();
                busy = false;
                if (plan != null) Review(); else Auth();
            } catch (Exception ex) when (ex is InvalidOperationException or FormatException) {
                message.Text = ex.Message;
                message.Foreground = Theme.Danger;
            } finally {
                busy = false;
                connect.IsEnabled = true;
                tabs.IsEnabled = true;
                fields.IsEnabled = true;
                connectButton.IsEnabled = true;
                RefreshConnection();
            }
        };
        p.Children.Add(connect);
        p.Children.Add(Txt("Senhas e tokens não são gravados no disco. Ao fechar, será necessário conectar novamente.", 12.5, Theme.Muted, top: 16));

        if (!demo) {
            var actions = new WrapPanel { Margin = new Thickness(0, 20, 0, 0) };
            actions.Children.Add(Btn("Usar demonstração", () => {
                if (!ConfirmDisconnect()) return;
                api.ClearToken();
                demo = true;
                RefreshConnection();
                Auth();
            }));
            p.Children.Add(actions);
        }

        var panel = Panel(p);
        panel.MaxWidth = 720;
        panel.HorizontalAlignment = HorizontalAlignment.Left;
        page.Children.Add(panel);
    }

    private const string PanelUrl = "https://painel.skymail.net.br";
    private const string SecretTutorialUrl = "https://ajuda.skymail.com.br/tutoriais/localizar-interface-api/";

    /// <summary>
    /// Ajuda da chave privada: o passo a passo fica dentro do aplicativo, para servir
    /// mesmo sem internet, e o link abre o tutorial oficial da Skymail para quem quiser as telas.
    /// </summary>
    private StackPanel SecretHelp() {
        var host = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };

        var details = new Border {
            Background = Theme.SurfaceAlt,
            BorderBrush = Theme.Line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(15, 13, 15, 13),
            Margin = new Thickness(0, 9, 0, 0),
            Visibility = Visibility.Collapsed
        };

        var steps = new StackPanel();
        steps.Children.Add(Txt("Onde encontrar a chave privada", 13, Theme.Ink, FontWeights.SemiBold));
        var passos = new[] {
            "Abra o painel Skymail em " + PanelUrl + " com um usuário administrador.",
            "Clique na seta no canto superior direito da tela.",
            "Escolha Configurações.",
            "Abra a aba Interface API — a chave privada da organização está ali."
        };
        for (int i = 0; i < passos.Length; i++) {
            var row = new Grid { Margin = new Thickness(0, i == 0 ? 10 : 7, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var number = Txt((i + 1) + ".", 12.5, Theme.Accent, FontWeights.SemiBold);
            var text = Txt(passos[i], 12.5, Theme.Muted);
            Grid.SetColumn(number, 0);
            Grid.SetColumn(text, 1);
            row.Children.Add(number);
            row.Children.Add(text);
            steps.Children.Add(row);
        }
        steps.Children.Add(Txt("Na documentação da Skymail essa chave aparece como SECRET KEY. Ela não é enviada à API: fica no seu computador apenas para assinar o token desta sessão.",
            12, Theme.Muted, top: 12));

        var open = Btn("Abrir o tutorial da Skymail", () => OpenUrl(SecretTutorialUrl));
        open.Margin = new Thickness(0, 14, 0, 0);
        steps.Children.Add(open);
        details.Child = steps;

        var toggle = new Button {
            Style = (Style)FindResource(Ui.LinkButton),
            ToolTip = "Mostrar o caminho no painel Skymail"
        };
        var toggleContent = new StackPanel { Orientation = Orientation.Horizontal };
        var toggleGlyph = new TextBlock {
            Text = "\uE897",
            FontFamily = new FontFamily(Ui.Icons),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0)
        };
        var toggleText = new TextBlock { Text = "Não sabe onde pegar a chave privada? Clique aqui.", VerticalAlignment = VerticalAlignment.Center };
        toggleContent.Children.Add(toggleGlyph);
        toggleContent.Children.Add(toggleText);
        toggle.Content = toggleContent;
        toggle.Click += (s, e) => {
            bool showing = details.Visibility == Visibility.Visible;
            details.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
            toggleText.Text = showing ? "Não sabe onde pegar a chave privada? Clique aqui." : "Ocultar as instruções da chave privada.";
        };

        host.Children.Add(toggle);
        host.Children.Add(details);
        return host;
    }

    /// <summary>
    /// Desconectar no meio de um lote deixa as chamadas seguintes sem token: a operação continuaria rodando e
    /// falhando uma conta atrás da outra. Aqui a pessoa é avisada antes, com a chance de voltar atrás.
    /// </summary>
    private bool ConfirmDisconnect() {
        var running = jobs.Values.Where(j => j.Running).Select(j => j.Name).ToArray();
        if (running.Length == 0) return true;
        return MessageBox.Show(this,
            "Ainda há operação em andamento: " + string.Join(", ", running) + ".\n\n" +
            "Ao desconectar, as próximas chamadas dessa operação vão falhar por falta de conexão, e os registros " +
            "restantes ficarão sem enviar.\n\nPare a operação em Processos antes, ou desconecte assim mesmo?",
            "SkyAPI — operação em andamento", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;
    }

    /// <summary>Abre um endereço no navegador padrão. Endereços são constantes do aplicativo.</summary>
    private void OpenUrl(string url) {
        try {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException) {
            MessageBox.Show(this,
                "Não foi possível abrir o navegador. Copie o endereço e abra manualmente:\n\n" + url,
                "SkyAPI", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static void Field(StackPanel parent, string label, Control control) {
        parent.Children.Add(new Label { Content = label, Margin = new Thickness(0, 15, 0, 6), Target = control });
        parent.Children.Add(control);
    }

    private void Input() {
        render = Input;
        Clear("home", OpName(), "Importe um CSV ou cole os registros. Os dados serão validados antes de continuar.");
        Steps(1);
        var p = new StackPanel();

        // Declarados aqui porque a conferência de senha, montada logo abaixo, precisa travar
        // o botão de avançar, que só é criado no fim da tela.
        Button? confirm = null;
        Action validatePassword = () => { };

        if (operation is Operation.PasswordSame or Operation.PasswordDifferent or Operation.ForcePasswordChange) {
            var modes = new ComboBox {
                ItemsSource = new[] { "Mesma senha para todas as contas", "Senha diferente por conta", "Exigir troca no próximo login" },
                SelectedIndex = operation == Operation.PasswordSame ? 0 : operation == Operation.PasswordDifferent ? 1 : 2
            };
            Field(p, "Como deseja gerenciar as senhas?", modes);
            modes.SelectionChanged += (s, e) => {
                inputText = "";
                sharedPassword = "";
                operation = modes.SelectedIndex switch { 0 => Operation.PasswordSame, 1 => Operation.PasswordDifferent, _ => Operation.ForcePasswordChange };
                Input();
            };
        }

        if (operation == Operation.AccountStatus) {
            var statuses = new ComboBox {
                ItemsSource = new[] { "Desabilitar conta", "Bloquear acesso", "Reativar conta" },
                SelectedIndex = Array.IndexOf(new[] { "disabled", "noaccess", "active" }, status)
            };
            statuses.SelectionChanged += (s, e) => status = new[] { "disabled", "noaccess", "active" }[statuses.SelectedIndex];
            Field(p, "Novo status", statuses);
        }

        if (operation == Operation.PasswordSame) {
            // Mesma conferência da Substituição de colaborador: quem entrega a senha precisa
            // poder vê-la e gerá-la, e uma senha fraca aqui falharia em todas as contas do lote
            // — melhor barrar antes de gastar milhares de chamadas à API.
            var pass = new PasswordBox { Password = sharedPassword };
            var plainPassword = new TextBox {
                Text = sharedPassword,
                Margin = new Thickness(0, 5, 0, 0),
                Visibility = Visibility.Collapsed
            };
            string Secret() => plainPassword.Visibility == Visibility.Visible ? plainPassword.Text : pass.Password;

            var feedback = Txt("", 12, Theme.Muted, top: 6);
            feedback.Name = "SharedPasswordFeedback";
            validatePassword = () => {
                var secret = Secret();
                sharedPassword = secret;
                // Sem endereços na comparação: a mesma senha vale para todas as contas do lote,
                // então não há um par de endereços a confrontar como há na substituição.
                var error = PasswordRules.ReplacementError(secret);
                bool valid = error == null;
                feedback.Text = (valid ? "Senha válida. " : "Senha incompleta. ") + secret.Length + "/8 caracteres (mínimo); " +
                    PasswordRules.TypeCount(secret) + "/3 tipos (mínimo).\n" +
                    (error ?? "Sem sequências. A política da organização ainda vale no envio.");
                feedback.Foreground = valid ? Theme.Ok : Theme.Muted;
                if (confirm != null) confirm.IsEnabled = valid;
            };
            pass.PasswordChanged += (s, e) => validatePassword();
            plainPassword.TextChanged += (s, e) => validatePassword();

            Field(p, "Nova senha (sujeita à política da organização)", pass);
            p.Children.Add(plainPassword);
            p.Children.Add(feedback);

            var passwordTools = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var reveal = Btn("Mostrar senha", () => { });
            reveal.Click += (s, e) => {
                bool showing = plainPassword.Visibility == Visibility.Visible;
                if (showing) {
                    pass.Password = plainPassword.Text;
                    plainPassword.Visibility = Visibility.Collapsed;
                    pass.Visibility = Visibility.Visible;
                } else {
                    plainPassword.Text = pass.Password;
                    pass.Visibility = Visibility.Collapsed;
                    plainPassword.Visibility = Visibility.Visible;
                }
                reveal.Content = showing ? "Mostrar senha" : "Ocultar senha";
                validatePassword();
            };
            passwordTools.Children.Add(reveal);
            passwordTools.Children.Add(Btn("Gerar senha", () => {
                var generated = PasswordRules.Generate();
                pass.Password = generated;
                plainPassword.Text = generated;
                validatePassword();
            }));
            p.Children.Add(passwordTools);
        }

        string format = operation switch {
            Operation.PasswordDifferent => "Um registro por linha: e-mail;senha. Tudo após o primeiro ponto e vírgula é a senha, incluindo espaços.",
            Operation.RenameAccounts => "Duas colunas, sem cabeçalho: e-mail atual,e-mail novo.",
            Operation.Attributes => "Com cabeçalho. mailbox é obrigatório; inclua apenas os atributos que deseja alterar.",
            Operation.DeleteDns => "Um domínio por linha, sem cabeçalho e sem https://.",
            _ => "Um e-mail por linha, sem cabeçalho."
        };
        p.Children.Add(Txt("Registros", 13, Theme.Ink, FontWeights.SemiBold, top: 19));
        p.Children.Add(Txt(format, 12.5, Theme.Muted, top: 5));

        var input = new TextBox {
            Text = inputText,
            AcceptsReturn = true,
            AcceptsTab = true,
            MinHeight = 165,
            MaxHeight = 250,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            VerticalContentAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 0, 0),
            AllowDrop = true
        };
        input.TextChanged += (s, e) => inputText = input.Text;
        input.PreviewDragOver += (s, e) => {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        input.PreviewDrop += (s, e) => {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length == 1) LoadFile(paths[0], input);
            e.Handled = true;
        };
        p.Children.Add(input);
        p.Children.Add(Txt("Você também pode arrastar o arquivo para a área acima.", 11.5, Theme.Muted, top: 8));

        var tools = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        tools.Children.Add(Btn("Selecionar CSV…", () => Import(input)));
        tools.Children.Add(Btn("Salvar modelo", SaveTemplate));
        if (demo) tools.Children.Add(Btn("Preencher exemplo", () => input.Text = Planner.Template(operation)));
        p.Children.Add(tools);

        var errors = Txt("", 13, Theme.Danger, top: 12);
        p.Children.Add(errors);

        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        actions.Children.Add(Btn("← Voltar", Home));
        confirm = Btn("Conferir registros →", () => {
            plan = Planner.Build(operation, input.Text, status, sharedPassword);
            if (plan.Errors.Count > 0) {
                errors.Text = string.Join("\n", plan.Errors.Take(15)) + (plan.Errors.Count > 15 ? "\n… e mais " + (plan.Errors.Count - 15) + " erro(s)." : "");
                return;
            }
            Review();
        }, true);
        actions.Children.Add(confirm);
        p.Children.Add(actions);
        validatePassword();   // estado inicial do aviso e do botão
        page.Children.Add(Panel(p));
    }

    private void Import(TextBox input) {
        var dialog = new OpenFileDialog { Filter = "CSV ou texto|*.csv;*.txt", Title = "Selecionar registros" };
        if (dialog.ShowDialog(this) == true) LoadFile(dialog.FileName, input);
    }

    private void LoadFile(string path, TextBox input) {
        try {
            if (new FileInfo(path).Length > 5_000_000) throw new InvalidOperationException("O arquivo excede 5 MB.");
            var bytes = File.ReadAllBytes(path);
            string text;
            try {
                text = new UTF8Encoding(false, true).GetString(bytes);
            } catch (DecoderFallbackException) {
                throw new InvalidOperationException("O arquivo não está em UTF-8. No Excel, use Salvar como → CSV UTF-8.");
            }
            input.Text = text.TrimStart('﻿');
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) {
            MessageBox.Show(this,
                ex is InvalidOperationException ? ex.Message : "Não foi possível ler o arquivo. Verifique se você tem acesso a ele.",
                "Importar dados", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveTemplate() {
        var dialog = new SaveFileDialog { FileName = "modelo-" + operation + ".csv", Filter = "Arquivo CSV|*.csv" };
        if (dialog.ShowDialog(this) == true) TrySave(dialog.FileName, Planner.Template(operation));
    }

    private void Review() {
        if (plan == null) return;
        render = Review;
        Clear("home", "Confira antes de executar", OpName() + " · " + plan.Items.Count + " registro(s) no lote");
        Steps(2);
        var p = new StackPanel();

        p.Children.Add(Txt(demo ? "Você está conferindo uma simulação." : "As alterações abaixo serão aplicadas nas contas indicadas.",
            15.5, Theme.Ink, FontWeights.SemiBold));
        p.Children.Add(Txt("Domínios: " + string.Join(", ", plan.Items.Select(x => x.Target.Contains('@') ? x.Target.Split('@')[1] : x.Target).Distinct().Take(8)),
            12.5, Theme.Muted, top: 7));
        foreach (var warning in plan.Warnings) p.Children.Add(Txt(warning, 12.5, Theme.Muted, top: 8));

        var grid = Table(new[] { ("Conta / domínio", "Target"), ("Alteração proposta", "Description") });
        grid.ItemsSource = plan.Items;
        grid.Margin = new Thickness(0, 16, 0, 14);
        p.Children.Add(grid);

        if (operation is Operation.DeleteGroups or Operation.DeleteDns)
            p.Children.Add(Banner("\uE7BA", "Exclusão permanente",
                "Esta operação não pode ser desfeita pelo aplicativo nem pela API.",
                Theme.DangerSoft, Theme.Danger));
        if (operation == Operation.DeleteAccounts)
            p.Children.Add(Txt("A recuperação de contas depende da disponibilidade e das regras do serviço. Não trate a exclusão como reversível garantida.",
                12.5, Theme.Muted));
        if (!demo && !api.HasToken)
            p.Children.Add(Txt("Conecte-se à API para executar. A configuração abre pelo botão no topo.", 13, Theme.Accent, top: 12));

        var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        actions.Children.Add(Btn("← Corrigir dados", Input));
        var run = Btn(demo ? "Simular execução" : "Executar lote", ConfirmRun, true);
        run.IsEnabled = demo || api.HasToken;
        actions.Children.Add(run);
        p.Children.Add(actions);
        page.Children.Add(Panel(p));
    }

    /// <summary>Cartão de operação: o mesmo da tela inicial, reaproveitado nos submenus.</summary>
    private Button OperationCard(string name, string description, string glyph, Action open) {
        var button = new Button {
            Style = (Style)FindResource(Ui.CardButton),
            Margin = new Thickness(0, 0, 14, 14), Height = 158
        };
        button.Click += (s, e) => open();
        var content = new StackPanel();
        content.Children.Add(new Border {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(11),
            Background = Theme.AccentSoft, HorizontalAlignment = HorizontalAlignment.Left,
            Child = Glyph(glyph, 19, Theme.Cyan)
        });
        content.Children.Add(Txt(name, 15.5, Theme.Ink, FontWeights.SemiBold, top: 16));
        var note = Txt(description, 12.5, Theme.Muted, top: 6);
        note.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(note);
        button.Content = content;
        return button;
    }

    private DataGrid Table((string heading, string field)[] columns) {
        var grid = new DataGrid {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            RowHeaderWidth = 0,
            Height = 300,
            MinRowHeight = 38,
            ColumnHeaderHeight = 40,
            FontSize = 13,
            EnableRowVirtualization = true,
            SelectionMode = DataGridSelectionMode.Single,
            // Copiar com Ctrl+C, por célula ou linha inteira: antes não dava para tirar nada da tabela.
            SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader,
            BorderThickness = new Thickness(1),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        foreach (var (heading, field) in columns)
            grid.Columns.Add(new DataGridTextColumn {
                Header = heading,
                Binding = new Binding(field),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 140,
                // O tooltip repete o mesmo campo: a célula corta com reticências, o tooltip mostra inteiro.
                ElementStyle = CellText(field)
            });
        return grid;
    }

    private static Style CellText(string field = "") {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        if (field.Length > 0) {
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(field)));
            style.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 60000));
            style.Setters.Add(new Setter(ToolTipService.InitialShowDelayProperty, 350));
        }
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Theme.Ink));
        return style;
    }

    private void ConfirmRun() {
        if (plan == null || busy) return;
        var dialog = new Window {
            Title = demo ? "Confirmar simulação" : "Confirmar operação",
            SizeToContent = SizeToContent.Height,
            Width = 520 * Theme.Scale,   // acompanha o tamanho de texto escolhido
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Theme.Bg,
            Foreground = Theme.Ink,
            FontFamily = FontFamily,
            FontSize = 14
        };
        Ui.Install(dialog);
        dialog.SourceInitialized += (s, e) => Native.TitleBar(dialog, Theme.Dark);

        var p = new StackPanel { Margin = new Thickness(26, 24, 26, 24), LayoutTransform = new ScaleTransform(Theme.Scale, Theme.Scale) };
        p.Children.Add(Txt(demo ? "Simular este lote?" : "Aplicar estas alterações?", 21, Theme.Heading, FontWeights.SemiBold));
        p.Children.Add(Txt(OpName() + " · " + plan.Items.Count + " registro(s)", 14.5, Theme.Ink, top: 12));
        p.Children.Add(Txt(demo
            ? "Nenhuma conta real será alterada."
            : "Digite EXECUTAR para confirmar. Parar o lote impede os próximos envios, mas não desfaz alterações já realizadas.",
            13, Theme.Muted, top: 10));

        var confirm = new TextBox { Margin = new Thickness(0, 14, 0, 0) };
        if (!demo) p.Children.Add(confirm);

        var row = new WrapPanel { Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var yes = Btn(demo ? "Simular" : "Confirmar execução", () => dialog.DialogResult = true, true);
        yes.Margin = new Thickness(0);
        yes.IsEnabled = demo;
        confirm.TextChanged += (s, e) => yes.IsEnabled = confirm.Text == "EXECUTAR";
        row.Children.Add(Btn("Cancelar", () => dialog.DialogResult = false));
        row.Children.Add(yes);
        p.Children.Add(row);

        dialog.Content = p;
        if (dialog.ShowDialog() == true) _ = RunBatch();
    }

    private async Task RunBatch() {
        if (plan == null || busy) return;
        var snapshot = plan.Items.ToArray();
        var skipped = Array.Empty<string>();
        if(!demo && snapshot.Any(i=>i.Path.EndsWith("/restore"))) {
            busy=true;connectButton.IsEnabled=false;
            try {
                var restoration=await new AdvancedService(api).RestorationPurchases(snapshot.Select(i=>i.Target));
                int count=restoration.PurchaseAccounts.Count;
                if(!ConfirmAdvanced("Conferência de licenças para restauração\n\n"+restoration.Summary+
                    (count>0?"\n\nAutoriza contratar até "+count+" licenças adicionais durante a restauração, com cobrança na conta Skymail?":""),
                    count>0?"Contratar até "+count+" licenças e restaurar":"Restaurar"))return;
                // A conferencia ja apurou que estas nao existem como excluidas. Enviar assim mesmo so
                // gastaria uma chamada e uma fatia do limite de requisicoes para colher o mesmo 404.
                skipped=restoration.Unrecognized.ToArray();
                snapshot=snapshot.Where(i=>!skipped.Contains(i.Target,StringComparer.OrdinalIgnoreCase))
                    .Select(i=>restoration.PurchaseAccounts.Contains(i.Target)
                    ?i with {Fields=new Dictionary<string,string>(i.Fields){["confirm_purchase"]="true"}}:i).ToArray();
            }catch(InvalidOperationException ex){MessageBox.Show(this,ex.Message,"Conferência de licenças");return;}
            finally {busy=false;connectButton.IsEnabled=true;}
        }
        busy = true;
        stopRequested = false;
        connectButton.IsEnabled = false;
        results.Clear();
        StreamWriter? writer = null;
        try {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Skynova", "SkyAPI", "Relatorios");
            Directory.CreateDirectory(dir);
            reportPath = Path.Combine(dir, (demo ? "simulacao-" : "lote-") + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".csv");
            writer = new StreamWriter(new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(true)) { AutoFlush = true };
            writer.WriteLine("data_hora;ambiente;conta;acao;status;http;mensagem");

            render = () => { };
            Clear("home", "Execução em andamento", OpName() + " · Aguarde a confirmação de cada registro.");
            Steps(3);
            var p = new StackPanel();
            var progressText = Txt("Preparando o lote…", 15.5, Theme.Ink, FontWeights.SemiBold);
            p.Children.Add(progressText);
            var progress = new ProgressBar { Minimum = 0, Maximum = snapshot.Length, Height = 9, Margin = new Thickness(0, 16, 0, 18) };
            p.Children.Add(progress);
            var grid = Table(new[] { ("Conta / domínio", "Target"), ("Resultado", "Status"), ("Detalhe", "Message") });
            grid.ItemsSource = results;
            p.Children.Add(grid);
            var stop = Btn("Parar após o registro atual", () => stopRequested = true);
            stop.Margin = new Thickness(0, 18, 0, 0);
            p.Children.Add(stop);
            page.Children.Add(Panel(p));

            await BatchRunner.RunAsync(snapshot, async item => {
                if (demo) {
                    await Task.Delay(180);
                    return new ApiResult(item.Target, item.Description, "Simulado", null, "Demonstração: nenhuma chamada à API.");
                }
                return await api.ExecuteAsync(item);
            }, result => {
                results.Add(result);
                writer.WriteLine(ResultLine(result));
                progress.Value = results.Count;
                progressText.Text = results.Count + " de " + snapshot.Length + " registros processados";
                grid.Items.Refresh();
                return Task.CompletedTask;
            }, () => stopRequested, 0);

            // Ficam no fim do relatorio, deixando claro que nao chegaram a ser enviadas.
            foreach (var account in skipped) {
                var row = new ApiResult(account, "Restaurar conta", "Não enviada", null,
                    "A API não reconhece esta conta como excluída. A conferência a deixou de fora do envio.");
                results.Add(row);
                writer.WriteLine(ResultLine(row));
            }
            if (skipped.Length > 0) grid.Items.Refresh();

            stop.IsEnabled = false;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) {
            MessageBox.Show(this,
                "O lote foi interrompido. Não foi possível preparar ou atualizar o relatório, ou a conexão deixou de estar disponível.\n\nConfira os resultados já exibidos e o painel antes de repetir.",
                "SkyAPI", MessageBoxButton.OK, MessageBoxImage.Warning);
            foreach (var item in snapshot.Skip(results.Count))
                results.Add(new(item.Target, item.Description, "Não enviado", null, "Interrompido antes do envio."));
        } finally {
            try { writer?.Dispose(); } catch (IOException) { }
            busy = false;
            connectButton.IsEnabled = true;
            inputText = "";
            sharedPassword = "";
            plan = null;
            Results();
        }
    }

    private string ResultLine(ApiResult r) => string.Join(";", new[] {
        r.Timestamp.ToString("O"), demo ? "Demonstração" : "API Skymail", r.Target, r.Action, r.Status, r.HttpStatus?.ToString() ?? "", r.Message
    }.Select(Csv.Cell));

    private void Results() {
        render = Results;
        Clear("home", "Resultado do lote", "Confira o resultado individual antes de iniciar outra operação.");
        Steps(3);
        bool succeeded=results.Count>0 && results.All(r=>r.Status is "Sucesso" or "Simulado");
        page.Children.Add(Banner(succeeded?"\uE73E":"\uE7BA",succeeded?"Operação concluída com sucesso":"Operação encerrada — confira os resultados",
            succeeded?"Todos os registros foram processados.":"Há registros que exigem conferência ou não foram enviados.",
            succeeded?Theme.OkSoft:Theme.WarnSoft,succeeded?Theme.Ok:Theme.Warn));
        var p = new StackPanel();

        var counts = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        for (int i = 0; i < 3; i++) counts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var tiles = new (int value, string label, Brush color)[] {
            (results.Count(r => r.Status is "Sucesso" or "Simulado"), demo ? "Simulados" : "Confirmados", Theme.Ok),
            (results.Count(r => r.Status is "Erro" or "Indeterminado" or "Pendente"), "Exigem conferência", Theme.Warn),
            (results.Count(r => r.Status == "Não enviado"), "Não enviados", Theme.Muted)
        };
        for (int i = 0; i < tiles.Length; i++) {
            var cell = new StackPanel();
            cell.Children.Add(Txt(tiles[i].value.ToString(), 30, tiles[i].color, FontWeights.SemiBold));
            cell.Children.Add(Txt(tiles[i].label, 12.5, Theme.Muted, top: 2));
            var tile = new Border {
                Background = Theme.SurfaceAlt,
                BorderBrush = Theme.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 13, 16, 13),
                Margin = new Thickness(i == 0 ? 0 : 6, 0, i == tiles.Length - 1 ? 0 : 6, 0),
                Child = cell
            };
            Grid.SetColumn(tile, i);
            counts.Children.Add(tile);
        }
        p.Children.Add(counts);

        var grid = Table(new[] { ("Conta / domínio", "Target"), ("Resultado", "Status"), ("Detalhe", "Message") });
        grid.ItemsSource = results;
        grid.SelectionChanged += (s, e) => {
            if (grid.SelectedItem is ApiResult r) detail.Text = r.Target + "\n" + r.Message;
        };
        p.Children.Add(grid);

        detail = Txt("Selecione uma linha para ler o detalhe completo.", 12.5, Theme.Muted, top: 12);
        p.Children.Add(detail);
        p.Children.Add(Txt("Indeterminado ou pendente não significa falha definitiva. Confira no painel antes de repetir. Nenhum registro é reenviado automaticamente.",
            12.5, Theme.Muted, top: 12));

        var actions = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
        actions.Children.Add(Btn("Nova operação", Home, true));
        actions.Children.Add(Btn("Salvar relatório…", () => {
            var d = new SaveFileDialog { FileName = Path.GetFileName(reportPath ?? "resultado.csv"), Filter = "CSV|*.csv" };
            if (d.ShowDialog(this) == true)
                TrySave(d.FileName, "data_hora;ambiente;conta;acao;status;http;mensagem\r\n" + string.Join("\r\n", results.Select(ResultLine)));
        }));
        p.Children.Add(actions);
        page.Children.Add(Panel(p));
    }

    private void TrySave(string path, string text) {
        try {
            File.WriteAllText(path, text, new UTF8Encoding(true));
            MessageBox.Show(this, "Arquivo salvo.", "SkyAPI", MessageBoxButton.OK, MessageBoxImage.Information);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            MessageBox.Show(this, "Não foi possível salvar. Escolha uma pasta na qual você possa gravar.", "SkyAPI", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Help() {
        if (busy) return;
        render = Help;
        Clear("help", "Ajuda e formatos", "Tudo o que você precisa para preparar seu lote.");
        var p = new StackPanel();
        var topics = new (string glyph, string heading, string body)[] {
            ("\uE72E", "1. Conexão", "Gere o token com usuário, senha e chave privada do painel, ou cole um JWT existente. A chave privada está no painel Skymail: clique na seta no canto superior direito, escolha Configurações e abra a aba Interface API — na documentação da Skymail ela aparece como SECRET KEY. O aplicativo usa apenas https://api.skymail.net.br/v1/ e as credenciais ficam na memória durante a sessão."),
            ("\uE8A5", "2. Arquivos", "Use CSV UTF-8, até 5 MB e 10.000 registros. Você pode arrastar o arquivo para a área de dados. Listas simples não têm cabeçalho. Renomeação aceita vírgula ou ponto e vírgula; senhas individuais usam e-mail;senha."),
            ("\uE77B", "3. Atributos", "Cabeçalho obrigatório: mailbox. Campos aceitos: Nome, Email secundario, Celular, Telefone residencial, Telefone comercial, Empresa, Unidade, Departamento, Cargo e Ramal. Também são aceitos os nomes técnicos da API. Campos vazios não apagam valores existentes. Telefones: 10 ou 11 dígitos, DDD + número, sem código de país."),
            ("\uE73E", "4. Conferência e execução", "Qualquer erro de validação bloqueia o lote. A execução exige confirmação explícita. Cada registro é enviado uma vez. Parar não desfaz alterações; aguarda o registro atual. Falhas de autenticação, limite de requisições, indisponibilidade ou resultado incerto interrompem os próximos envios."),
            ("\uE9F9", "5. Relatórios", "Cada resultado é salvo automaticamente em %LOCALAPPDATA%\\Skynova\\SkyAPI\\Relatorios. O relatório contém contas e resultados, nunca senhas, tokens ou respostas brutas da API. Use Salvar relatório para escolher outra pasta."),
            ("\uE768", "6. Demonstração", "Experimente todos os fluxos sem acesso à API pela tela de conexão. Os relatórios indicam claramente que os resultados foram simulados."),
            ("\uE706", "Aparência", "O tema claro ou escuro e o tamanho do texto ficam no rodapé do menu lateral. A escolha é lembrada e reaplicada na próxima abertura."),
            ("\uE946", "Sobre esta versão", "SkyAPI " + Version + " — versão para homologação interna. Operações baseadas no script api-requests.ps1 fornecido. A aceitação dos dados e as permissões finais dependem da API. Logo e paleta obtidos do site oficial Skynova (skynova.com.br).")
        };

        bool first = true;
        foreach (var (glyph, heading, body) in topics) {
            var row = new Grid { Margin = new Thickness(0, first ? 0 : 20, 0, 0) };
            first = false;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var badge = new Border {
                Width = 34, Height = 34,
                CornerRadius = new CornerRadius(9),
                Background = Theme.AccentSoft,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 14, 0),
                Child = Glyph(glyph, 15, Theme.Accent)
            };
            Grid.SetColumn(badge, 0);
            row.Children.Add(badge);

            var texts = new StackPanel();
            texts.Children.Add(Txt(heading, 15.5, Theme.Ink, FontWeights.SemiBold));
            texts.Children.Add(Txt(body, 13, Theme.Muted, top: 6));
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);

            p.Children.Add(row);
        }
        page.Children.Add(Panel(p));
    }

    private void OnClosing(object? sender, CancelEventArgs e) {
        if (busy) {
            stopRequested = true;
            e.Cancel = true;
            MessageBox.Show(this,
                "Aguarde a conclusão do registro atual. Os próximos envios serão interrompidos e o relatório será finalizado.",
                "Lote em andamento", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
