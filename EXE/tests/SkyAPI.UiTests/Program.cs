using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using SkyAPI.Desktop;

internal static class Program {
    private static int checks;
    private static readonly List<string> failures = new();

    [STAThread]
    private static int Main() {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (bool dark in new[] { false, true })
        foreach (double scale in Theme.Scales) {
            Theme.Apply(dark);
            Theme.SetScale(scale);
            var window = new MainWindow {
                Width = 1320, Height = 1000, Left = -4000, Top = 0,
                WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false
            };
            window.Show();
            foreach (string screen in new[] { "licenses", "upgrade", "groups", "replace", "received", "sent", "groupedit" }) {
                typeof(MainWindow).GetMethod("Advanced", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, new object[] { screen, "", "", "" });
                Pump(window);
                if (screen == "licenses") {
                    var products = Find<ComboBox>(window).Single(b => b.DisplayMemberPath == "Name");
                    var consult=Find<Button>(window).Single(b=>Equals(b.Content,"1. Consultar produtos do cliente"));
                    var accountBox=Find<TextBox>(window).Single(b=>b.AcceptsReturn);
                    Check(!Find<TextBox>(window).Any(b=>Ui.GetHint(b)=="empresa.com.br"),"1.1.11: licencas sem campo dominio");
                    Check(consult.TranslatePoint(new Point(),window).Y < products.TranslatePoint(new Point(),window).Y &&
                        products.TranslatePoint(new Point(),window).Y < accountBox.TranslatePoint(new Point(),window).Y,
                        "1.1.11: consulta, produto e contas nessa ordem");
                    Check(!Find<ProgressBar>(window).Any(b=>b.IsVisible),"1.1.11: tela inicial sem progresso antigo");
                    products.ItemsSource = new[] { new SkyAPI.Core.MailProduct("SkyMail Premium 50GB", "7", "email") };
                    products.SelectedIndex = 0;
                    Pump(window);
                    if(dark && scale==1.0)SaveWindow(window,"licencas-1.1.11");
                    var selected = (ContentPresenter)products.Template.FindName("content", products);
                    Check(Find<TextBlock>(selected).Any(t => t.Text == "SkyMail Premium 50GB"),
                        $"licenses/{dark}/{scale}: selecao mostra apenas o nome do produto");
                }
                var export = Find<Button>(window).First(b => Equals(b.Content, "Exportar resultados CSV"));
                Check(export.Margin.Top >= 12, $"{screen}/{dark}/{scale}: espaco antes da exportacao");
                if (screen == "replace") {
                    var password = Find<PasswordBox>(window).Single();
                    var execute = Find<Button>(window).Single(b => Equals(b.Content, "Conferir e executar"));
                    var feedback = Find<TextBlock>(window).Single(b => b.Name == "PasswordFeedback");
                    Check(!execute.IsEnabled, "1.1.9: senha vazia desabilita execucao");
                    password.Password = "12345678901234";
                    Check(!execute.IsEnabled && feedback.Text.Contains("1/3 tipos"), "1.1.9: senha numerica invalida ao digitar");
                    password.Password = "Rmvn2958";
                    Check(execute.IsEnabled && feedback.Text.Contains("Senha válida"), "1.1.9: tres tipos habilitam execucao");
                    var reveal = Find<Button>(window).Single(b => Equals(b.Content, "Mostrar senha"));
                    reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var plain = Find<TextBox>(window).Single(b => b.Text == "Rmvn2958");
                    plain.Text = "Abc123";
                    Check(!execute.IsEnabled, "1.1.9: senha visivel curta desabilita execucao");
                    plain.Text = "rmvn295!";
                    Check(execute.IsEnabled, "1.1.9: validacao atualiza tambem na senha visivel");
                    reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(password.Password == "rmvn295!" && execute.IsEnabled, "1.1.9: ocultar preserva senha e validacao");
                    var newAddress=Find<TextBox>(window).Single(b=>Ui.GetHint(b)=="ana.silva@empresa.com.br");
                    newAddress.Text="rmvn@empresa.com.br";
                    Check(!execute.IsEnabled && feedback.Text.Contains("conta"),"1.1.10: alterar endereco revalida senha");
                    var generate=Find<Button>(window).Single(b=>Equals(b.Content,"Gerar senha"));
                    generate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(password.Password.Length==10 && execute.IsEnabled,"1.1.10: gerador preenche senha oculta valida");
                    reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var previous=plain.Text;
                    generate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(plain.Text.Length==10 && plain.Text!=previous && execute.IsEnabled,"1.1.10: gerador preenche senha visivel valida");
                    reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    newAddress.Clear();
                    password.Clear();
                }
                var boxes = Find<TextBox>(window).Where(b => Ui.GetHint(b).Length > 0).ToArray();
                foreach (var box in boxes) {
                    string name = $"{screen}/{dark}/{scale}/{Ui.GetHint(box).Split('\n')[0]}";
                    box.BringIntoView();
                    Pump(window);
                    Check(box.Text.Length == 0, name + ": exemplo nao pode ser valor real");
                    CheckAlignment(box, name + "/vazio");
                    box.Focus();
                    Pump(window);
                    CheckAlignment(box, name + "/foco");
                    Check(box.CaretIndex == 0, name + ": cursor deve comecar em zero");
                    box.SelectedText = "a";
                    Pump(window);
                    Check(Hint(box).Visibility != Visibility.Visible, name + ": exemplo deve sumir ao digitar");
                    box.SelectAll();
                    box.SelectedText = "";
                    Pump(window);
                    CheckAlignment(box, name + "/apagado");
                    box.SelectedText = box.AcceptsReturn ? "ana@empresa.com.br\r\nbia@empresa.com.br" : "empresa.com.br";
                    Pump(window);
                    Check(Hint(box).Visibility != Visibility.Visible, name + ": exemplo deve sumir com texto inserido em bloco");
                    box.Clear();
                    Pump(window);
                    CheckAlignment(box, name + "/limpo");
                    if (screen == "upgrade" && dark && scale == 1.0) Save(box, box.AcceptsReturn ? "contas" : "dominio");
                }
            }
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;

            // 1.1.12: Gerenciar senhas ganhou a mesma conferencia de senha da Substituicao de colaborador.
            void OpenPasswords(SkyAPI.Core.Operation mode) {
                typeof(MainWindow).GetField("sharedPassword",flags)!.SetValue(window,"");
                typeof(MainWindow).GetField("operation",flags)!.SetValue(window,mode);
                typeof(MainWindow).GetMethod("Input",flags)!.Invoke(window,null);
                Pump(window);
            }
            OpenPasswords(SkyAPI.Core.Operation.PasswordSame);
            {
                var shared=Find<PasswordBox>(window).Single();
                var advance=Find<Button>(window).Single(b=>Equals(b.Content,"Conferir registros →"));
                var note=Find<TextBlock>(window).Single(b=>b.Name=="SharedPasswordFeedback");
                Check(!advance.IsEnabled && note.Text.Contains("0/8 caracteres"),"1.1.12: senha compartilhada vazia trava a conferencia");
                shared.Password="12345678901234";
                Check(!advance.IsEnabled && note.Text.Contains("1/3 tipos"),"1.1.12: senha compartilhada numerica invalida ao digitar");
                shared.Password="Rmvn2958";
                Check(advance.IsEnabled && note.Text.Contains("Senha válida") && note.Foreground==Theme.Ok,"1.1.12: tres tipos liberam a conferencia");
                Check(typeof(MainWindow).GetField("sharedPassword",flags)!.GetValue(window) as string=="Rmvn2958","1.1.12: senha digitada chega ao lote");
                var showShared=Find<Button>(window).Single(b=>Equals(b.Content,"Mostrar senha"));
                showShared.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var sharedPlain=Find<TextBox>(window).Single(b=>b.Text=="Rmvn2958");
                sharedPlain.Text="Abc123";
                Check(!advance.IsEnabled,"1.1.12: senha compartilhada visivel curta trava a conferencia");
                sharedPlain.Text="rmvn295!";
                Check(advance.IsEnabled,"1.1.12: validacao atualiza tambem na senha visivel");
                showShared.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(shared.Password=="rmvn295!" && advance.IsEnabled,"1.1.12: ocultar preserva a senha compartilhada");
                var generateShared=Find<Button>(window).Single(b=>Equals(b.Content,"Gerar senha"));
                generateShared.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(shared.Password.Length==10 && advance.IsEnabled,"1.1.12: gerador preenche senha compartilhada valida");
                Check(typeof(MainWindow).GetField("sharedPassword",flags)!.GetValue(window) as string==shared.Password,"1.1.12: senha gerada chega ao lote");
                if(dark && scale==1.0){Pump(window);SaveWindow(window,"senhas-1.1.12");}
            }
            // Sem campo de senha, a conferencia nao pode ficar travada pela regra de senha.
            foreach(var mode in new[]{SkyAPI.Core.Operation.PasswordDifferent,SkyAPI.Core.Operation.ForcePasswordChange}) {
                OpenPasswords(mode);
                Check(!Find<PasswordBox>(window).Any(),"1.1.12: "+mode+" nao mostra campo de senha unica");
                Check(Find<Button>(window).Single(b=>Equals(b.Content,"Conferir registros →")).IsEnabled,"1.1.12: "+mode+" avanca sem senha unica");
            }
            typeof(MainWindow).GetField("sharedPassword",flags)!.SetValue(window,"");

            var job=typeof(MainWindow).GetMethod("Job",flags)!.Invoke(window,new object[]{"licenses"})!;
            void Set(string field,object value)=>job.GetType().GetField(field)!.SetValue(job,value);
            Set("Started",DateTime.Now.AddMinutes(-1));Set("Finished",DateTime.Now);
            Set("Status","Consulta concluída de teste.");
            var jobRows=(System.Collections.ObjectModel.ObservableCollection<SkyAPI.Core.OperationRow>)job.GetType().GetField("Rows")!.GetValue(job)!;
            jobRows.Add(new("ana@empresa.com.br","Sucesso","Detalhe preservado"));
            void OpenLicense()=>typeof(MainWindow).GetMethod("Advanced",flags)!.Invoke(window,new object[]{"licenses","","",""});
            OpenLicense();Pump(window);
            Check(!Find<TextBlock>(window).Any(t=>t.IsVisible && t.Text=="Consulta concluída de teste."),"1.1.11: voltar pelo menu oculta status encerrado");
            Check(jobRows.Count==1,"1.1.11: navegar preserva resultados");
            typeof(MainWindow).GetMethod("Processes",flags)!.Invoke(window,null);Pump(window);
            Find<Button>(window).Single(b=>Equals(b.Content,"Abrir")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(window);
            Check(Find<TextBlock>(window).Any(t=>t.IsVisible && t.Text=="Consulta concluída de teste." && t.Foreground==Theme.Ok),"1.1.11: Processos reabre detalhes com sucesso verde");
            if(dark && scale==1.0)SaveWindow(window,"processo-concluido-1.1.11");
            Set("Running",true);OpenLicense();Pump(window);
            Check(Find<ProgressBar>(window).Any(p=>p.IsVisible),"1.1.11: andamento continua visivel ao retornar");
            Set("Running",false);Set("Failed",true);job.GetType().GetMethod("Report")!.Invoke(job,null);Pump(window);
            Check(Find<TextBlock>(window).Any(t=>t.IsVisible && t.Text=="Consulta concluída de teste." && t.Foreground==Theme.Warn),"1.1.11: erro nao recebe sucesso verde");
            Set("Started",default(DateTime));
            window.Close();
        }
        Console.WriteLine($"Verificacoes: {checks}; falhas: {failures.Count}");
        foreach (string failure in failures.Take(30)) Console.WriteLine(failure);
        app.Shutdown();
        return failures.Count == 0 ? 0 : 1;
    }

    private static TextBlock Hint(TextBox box) => (TextBlock)box.Template.FindName("hint", box);

    private static void CheckAlignment(TextBox box, string name) {
        var hint = Hint(box);
        Check(hint.IsVisible, name + ": exemplo deve aparecer no campo vazio");
        Rect caret = box.GetRectFromCharacterIndex(0);
        Rect first = hint.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        Point origin = hint.TranslatePoint(first.TopLeft, box);
        Check(!caret.IsEmpty && Math.Abs(caret.X - origin.X) <= 0.75 && Math.Abs(caret.Y - origin.Y) <= 0.75,
            $"{name}: cursor {caret.X:F2},{caret.Y:F2}; exemplo {origin.X:F2},{origin.Y:F2}");
    }

    private static void Check(bool ok, string message) {
        checks++;
        if (!ok) failures.Add(message);
    }

    private static void Pump(Window window) {
        for (int i = 0; i < 3; i++) {
            window.UpdateLayout();
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static void SaveWindow(Window window,string name) {
        var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var dir=Path.Combine(AppContext.BaseDirectory,"screenshots");Directory.CreateDirectory(dir);
        using var file=File.Create(Path.Combine(dir,name+".png"));encoder.Save(file);
    }

    private static void Save(TextBox box, string name) {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(box.ActualWidth), (int)Math.Ceiling(box.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(box);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(dir, name + ".png"));
        encoder.Save(file);
    }
}
