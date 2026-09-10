using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SkyAPI.Core;

namespace SkyAPI.Desktop;
public sealed partial class MainWindow {
    // Um lote continua rodando enquanto a pessoa navega: o estado vive na janela, não na página, e cada módulo
    // tem o seu. Resultados, status e o pedido de parada acompanham a tarefa, não a tela aberta.
    private sealed class JobState {
        public string Kind="",Name="";
        public readonly ObservableCollection<OperationRow> Rows=new();
        public readonly ObservableCollection<MessageRow> Messages=new();
        public string Status="Pronto para conferir.";
        public double Progress,Maximum=1;
        public bool Running,StopRequested,Failed;
        public DateTime Started,Finished;
        public event Action? Changed;
        public void Report()=>Changed?.Invoke();
    }
    private JobState? processDetails;
    private readonly Dictionary<string,JobState> jobs=new(StringComparer.Ordinal);
    private JobState Job(string kind) {
        if(!jobs.TryGetValue(kind,out var job)){job=new JobState{Kind=kind,Name=AdvancedName(kind)};jobs[kind]=job;}
        return job;
    }
    private bool AnyJobRunning=>jobs.Values.Any(j=>j.Running);
    // Erro que sabe qual campo está errado, para destacar o campo além de mostrar a mensagem.
    private sealed class FieldError:InvalidOperationException {
        public Control? Field {get;}
        public FieldError(string message,Control? field=null):base(message){Field=field;}
    }
    private static InvalidOperationException Fail(string message,Control? field=null)=>new FieldError(message,field);
    // These modules read and change live data: there is nothing to simulate, so demonstration says so plainly.
    private const string AddProductUrl=
        "https://suporte.skynova.com.br/hc/pt-br/articles/6559217444244-Adicionar-produto-no-painel-de-controle";
    private const string DemoNote="Este módulo consulta e altera dados reais na API, então não tem simulação. "+
        "Saia da demonstração e conecte-se à API para usá-lo.";
    // Tela de processos: o que está rodando, desde quando, com o botão de parar de cada um.
    private void Processes() {
        render=Processes;
        Clear("jobs","Processos","Operações em andamento e as últimas concluídas.");
        var list=new StackPanel();
        page.Children.Add(list);
        void Fill() {
            list.Children.Clear();
            var known=jobs.Values.Where(j=>j.Running||j.Started!=default).OrderByDescending(j=>j.Running)
                .ThenByDescending(j=>j.Started).ToArray();
            if(known.Length==0) {
                list.Children.Add(Panel(Txt("Nenhuma operação foi iniciada nesta sessão.",14,Theme.Muted)));
                return;
            }
            foreach(var job in known) {
                var card=new StackPanel();
                var head=new StackPanel{Orientation=Orientation.Horizontal};
                head.Children.Add(Txt(job.Name,15,Theme.Ink,FontWeights.SemiBold));
                var badge=job.Running?(job.StopRequested?"parando":"em andamento"):"concluída";
                head.Children.Add(Txt("   "+badge,13,job.Running?Theme.Accent:Theme.Muted));
                card.Children.Add(head);
                var span=(job.Running?DateTime.Now:job.Finished)-job.Started;
                card.Children.Add(Txt("Início "+job.Started.ToString("HH:mm:ss")+" · "+
                    (span.TotalSeconds<0?0:(int)span.TotalSeconds)+" s · "+job.Rows.Count+" registros"+
                    (job.Messages.Count>0?" · "+job.Messages.Count+" mensagens":""),13,Theme.Muted,top:4));
                card.Children.Add(Txt(job.Status,13,Theme.Muted,top:4));
                var actions=new WrapPanel{Margin=new Thickness(0,10,0,0)};
                actions.Children.Add(Btn("Abrir",()=>{processDetails=job;Advanced(job.Kind);}));
                if(job.Running&&!job.StopRequested)
                    actions.Children.Add(Btn("Parar após a chamada atual",()=>{job.StopRequested=true;job.Report();Fill();}));
                card.Children.Add(actions);
                list.Children.Add(Panel(card));
            }
        }
        Fill();
        // Um segundo basta para o tempo decorrido andar sem pesar; o timer morre ao sair da tela.
        processTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        processTimer.Tick+=(s,e)=>Fill();
        processTimer.Start();
    }
    private void UpdateJobsBadge() {
        if(!navButtons.TryGetValue("jobs",out var button))return;
        int running=jobs.Values.Count(j=>j.Running);
        if(button.Content is Grid grid && grid.Children.Count>1 && grid.Children[1] is TextBlock label)
            label.Text=running>0?"Processos ("+running+")":"Processos";
    }
    // Os submenus usam o mesmo cartão da tela inicial: antes eram botões soltos num canto da tela.
    private void SubMenu(string key,string title,string description,params (string Kind,string Name,string Note,string Glyph)[] items) {
        if(busy)return;
        Clear(key,title,description,DemoNote);
        var wrap=new UniformGrid{Columns=2,Margin=new Thickness(0,4,-14,0)};
        foreach(var item in items)wrap.Children.Add(OperationCard(item.Name,item.Note,item.Glyph,()=>Advanced(item.Kind)));
        page.Children.Add(wrap);
    }
    private void LicenseMenu() {
        render=LicenseMenu;
        SubMenu("licenses","Licenças","Troca de produto e análise de uso, sempre com conferência antes de enviar.",
            ("licenses","Alteração em lote","Troque o produto de várias caixas de uma vez, com conferência conta a conta.","\uE8D7"),
            ("upgrade","Análise para Upgrade","Encontre caixas perto do limite e veja o produto sugerido. Não altera nada.","\uE9D9"));
    }
    private void ReportMenu() {
        render=ReportMenu;
        SubMenu("reports","Relatórios","Buscas cruzadas de mensagens, por domínio ou por contas, que o painel não oferece.",
            ("received","Mensagens recebidas","Quem mandou para o seu domínio ou para contas específicas.","\uE715"),
            ("sent","Mensagens enviadas","Para onde o seu domínio ou suas contas mandaram mensagens.","\uE724"));
    }
    private void GroupMenu() {
        render=GroupMenu;
        SubMenu("groups","Grupos","Criação em lote e manutenção de quem participa de cada grupo.",
            ("groups","Criar grupos em lote","Importe grupos com membros, escritores e moderadores.","\uE716"),
            ("groupedit","Gerenciar membros","Inclua ou remova pessoas de vários grupos de uma vez.","\uE748"));
    }
    private static string AdvancedName(string kind)=>kind switch {
        "licenses"=>"Alteração de licenças em lote","upgrade"=>"Análise para Upgrade",
        "received"=>"Mensagens recebidas","sent"=>"Mensagens enviadas",
        "groups"=>"Criação de grupos em lote","groupedit"=>"Gerenciar membros de grupos",
        _=>"Substituição de colaborador"
    };
    // Cada tela diz, em uma frase, o que preencher e o que vai acontecer. Antes só havia o rótulo do campo.
    private static string Guidance(string kind)=>kind switch {
        "licenses"=>"Consulte os produtos disponíveis na conexão, selecione o produto de destino e cole as contas — uma por linha. O aplicativo lê cada caixa, mostra o que muda e só envia depois da sua confirmação.",
        "upgrade"=>"Informe o domínio e o uso mínimo. Deixe a lista de contas vazia para o aplicativo procurar as caixas do domínio pela própria API; preencha apenas se quiser limitar a análise a algumas contas. Nada é alterado: o resultado é uma sugestão, que você pode enviar para a alteração de licenças.",
        "groups"=>"Uma linha por grupo, ou várias linhas com o mesmo e-mail de grupo para ir somando pessoas. As colunas de pessoas aceitam vários endereços separados por | ou uma pessoa por linha.",
        "groupedit"=>"Uma linha por alteração: o grupo, se é para adicionar ou remover, o papel da pessoa e o endereço. Serve para tirar muita gente de muitos grupos de uma vez. O grupo em si não é apagado.",
        "replace"=>"Renomeia a conta atual para o novo endereço, troca a senha e decide se o endereço antigo continua valendo como apelido. A renomeação pode levar até 10 minutos, e o aplicativo acompanha até 11.",
        _=>"Escolha, de cada lado, se vai buscar por domínio ou por contas de e-mail, e preencha a caixa. "+
            "Domínio é um por vez; contas podem ser várias, uma por linha. Deixe um lado vazio para não filtrar por ele. "+
            "Quando qualquer um dos lados for domínio, o período é de até 7 dias."
    };
    private static string SampleCsv(string kind)=>kind switch {
        "groups"=>"grupo,email,membros,escritores,moderadores\r\n"+
            "Financeiro,financeiro@empresa.com.br,ana@empresa.com.br|bia@empresa.com.br,ana@empresa.com.br,\r\n"+
            "Suporte,suporte@empresa.com.br,carlos@empresa.com.br,,\r\n"+
            "Suporte,suporte@empresa.com.br,daniela@empresa.com.br,,\r\n",
        "groupedit"=>BatchInput.GroupEditHeader+"\r\n"+
            "financeiro@empresa.com.br,remover,membro,ana@empresa.com.br\r\n"+
            "financeiro@empresa.com.br,remover,membro,bia@empresa.com.br\r\n"+
            "suporte@empresa.com.br,adicionar,membro,carlos@empresa.com.br\r\n"+
            "suporte@empresa.com.br,adicionar,moderador,carlos@empresa.com.br\r\n",
        _=>"conta\r\nana@empresa.com.br\r\nbia@empresa.com.br\r\ncarlos@empresa.com.br\r\n"
    };
    private TextBox InputField(StackPanel host,string label,string value="",bool multiline=false,string hint="") {
        host.Children.Add(Txt(label,13,Theme.Muted,top:10));
        var box=new TextBox {Text=value,AcceptsReturn=multiline,Margin=new Thickness(0,5,0,10),
            Height=multiline?125:double.NaN,
            VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden,
            // Sem isto o cursor de um campo de várias linhas nasce no meio da caixa, e não na primeira linha.
            VerticalContentAlignment=multiline?VerticalAlignment.Top:VerticalAlignment.Center};
        // O exemplo vive dentro do template do campo, na mesma caixa de padding do texto real.
        Ui.SetHint(box,hint);
        host.Children.Add(box);
        return box;
    }
    private void Advanced(string kind,string preset="",string presetDomain="",string presetProduct="") {
        if(busy)return;
        render=()=>Advanced(kind,preset,presetDomain,presetProduct);
        var nav=kind is "received" or "sent"?"reports":kind=="upgrade"?"licenses":kind=="groupedit"?"groups":kind;
        Clear(nav,AdvancedName(kind),"Confira os dados antes de executar. Todos os resultados podem ser exportados em CSV.",DemoNote);
        var form=new StackPanel();
        form.Children.Add(Txt(Guidance(kind),13,Theme.Muted,top:2));
        var domain=kind=="upgrade"?InputField(form,"Domínio",presetDomain,hint:"empresa.com.br"):new TextBox();
        var accounts=kind is "received" or "sent"?new TextBox():InputField(form,
            kind=="groups"?"CSV dos grupos — cabeçalho grupo,email,membros,escritores,moderadores":
            kind=="groupedit"?"CSV das alterações — cabeçalho "+BatchInput.GroupEditHeader:
            kind=="replace"?"Conta atual":
            kind=="upgrade"?"Contas (opcional) — deixe vazio para analisar o domínio inteiro":
            "Contas — uma por linha; CSV/TXT, vírgula, ponto e vírgula ou TAB",
            preset,kind!="replace",
            kind=="groups"?"grupo,email,membros,escritores,moderadores\nFinanceiro,financeiro@empresa.com.br,ana@empresa.com.br|bia@empresa.com.br,,":
            kind=="groupedit"?BatchInput.GroupEditHeader+"\nfinanceiro@empresa.com.br,remover,membro,ana@empresa.com.br":
            kind=="replace"?"ana@empresa.com.br":
            "ana@empresa.com.br\nbia@empresa.com.br");
        if(kind is not ("replace" or "received" or "sent")) {
            var files=new WrapPanel();
            files.Children.Add(Btn("Importar CSV / TXT",()=>{
                var picker=new OpenFileDialog {Filter="CSV ou TXT|*.csv;*.txt"};
                if(picker.ShowDialog(this)!=true)return;
                try {
                    if(new FileInfo(picker.FileName).Length>5_000_000)throw new InvalidOperationException("Limite de arquivo: 5 MB.");
                    accounts.Text=File.ReadAllText(picker.FileName,new UTF8Encoding(false,true));
                }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or DecoderFallbackException or InvalidOperationException){
                    MessageBox.Show(this,"Não foi possível importar. Use UTF-8 e arquivo de até 5 MB.","SkyAPI");
                }
            }));
            files.Children.Add(Btn("Baixar CSV de exemplo",()=>{
                var save=new SaveFileDialog{Filter="CSV UTF-8|*.csv",FileName="SkyAPI_modelo_"+kind+".csv"};
                if(save.ShowDialog(this)==true)TrySave(save.FileName,SampleCsv(kind));
            }));
            form.Children.Add(files);
        }
        // A lista nasce desabilitada porque ainda não há catálogo; consultar o domínio é o que a habilita.
        var products=new ComboBox {Margin=new Thickness(0,5,0,10),DisplayMemberPath="Name",IsEnabled=false};
        IReadOnlyList<MailProduct> catalog=Array.Empty<MailProduct>();
        IReadOnlyList<ClientLicenseProduct> clientCatalog=Array.Empty<ClientLicenseProduct>();
        if(kind=="licenses") {
            form.Children.Add(Txt("2. Produto de destino",13,Theme.Muted,top:10));
            form.Children.Add(products);
            form.Children.Add(Txt("A lista traz apenas os produtos já contratados pelo cliente no painel Skymail. "+
                "Se o produto que você quer não aparecer, é porque ele ainda não existe no painel; adicione por lá e consulte de novo.",12,Theme.Muted));
            var help=Btn("Como adicionar um produto no painel",()=>OpenUrl(AddProductUrl));
            help.Style=(Style)FindResource(Ui.LinkButton);
            form.Children.Add(help);
        }
        var threshold=new ComboBox {IsEditable=true,ItemsSource=new[]{"70","80","90","95"},Text="80",Margin=new Thickness(0,5,0,10)};
        if(kind=="upgrade") {
            form.Children.Add(Txt("Uso mínimo (%) — escolha na lista ou digite um percentual",13,Theme.Muted));
            form.Children.Add(threshold);
            form.Children.Add(Txt("A sugestão mantém a família da conta: SkyMail (5, 25, 50, 100, 200 e 500 GB) ou "+
                "SkyExchange (Basic 5 e 25 GB; 50, 100 e 200 GB). "+
                "Se o produto sugerido ainda não estiver contratado no painel do cliente, a análise avisa — "+
                "adicione por lá e consulte de novo para poder enviar a alteração.",12,Theme.Muted));
            var panelHelp=Btn("Como adicionar um produto no painel",()=>OpenUrl(AddProductUrl));
            panelHelp.Style=(Style)FindResource(Ui.LinkButton);
            form.Children.Add(panelHelp);
        }
        Action refreshPeriod=()=>{};
        // Cada lado da busca escolhe entre um domínio ou uma lista de contas, e o exemplo acompanha a escolha.
        (Func<bool> IsDomain,TextBox Box) BuildSide(string label) {
            form.Children.Add(Txt(label,13,Theme.Muted,top:12));
            var mode=new ComboBox{ItemsSource=new[]{"Domínio — um por vez","Contas de e-mail — uma por linha"},
                SelectedIndex=0,Margin=new Thickness(0,5,0,6)};
            form.Children.Add(mode);
            var box=new TextBox{AcceptsReturn=true,Height=84,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,0,0,4),
                VerticalContentAlignment=VerticalAlignment.Top};
            // O exemplo acompanha a opção escolhida e vive dentro do próprio campo.
            void SyncSide()=>Ui.SetHint(box,mode.SelectedIndex==0?"empresa.com.br":"ana@empresa.com.br");
            mode.SelectionChanged+=(s,e)=>{SyncSide();refreshPeriod();};SyncSide();
            // O limite depende do que está escrito, não só da opção escolhida: digitar o domínio já encurta.
            box.TextChanged+=(s,e)=>refreshPeriod();
            form.Children.Add(box);
            return (()=>mode.SelectedIndex==0,box);
        }
        (Func<bool> IsDomain,TextBox Box) originSide=(()=>true,new TextBox()),destinationSide=(()=>true,new TextBox());
        // Lado por domínio só conta quando tem conteúdo: caixa vazia não filtra nada e não encurta o período.
        static bool DomainSide((Func<bool> IsDomain,TextBox Box) side)=>side.IsDomain()&&side.Box.Text.Trim().Length>0;
        if(kind is "received" or "sent") {
            originSide=BuildSide("Remetente"+(kind=="sent"?"":" (opcional)"));
            destinationSide=BuildSide("Destinatário"+(kind=="received"?"":" (opcional)"));
        }
        var dateFrom=new DatePicker {SelectedDate=DateTime.Today.AddDays(-6),Margin=new Thickness(0,4,0,8)};
        var dateTo=new DatePicker {SelectedDate=DateTime.Today,Margin=new Thickness(0,4,0,8)};
        // Períodos prontos, e as opções longas ficam desabilitadas quando o lado principal é um domínio.
        var spans=new (string Label,int Days)[]{("Hoje",1),("Ontem",1),("Últimos 7 dias",7),("Esta semana",7),
            ("Últimos 30 dias",30),("Últimos 90 dias",90),("Personalizado",0)};
        var period=new ComboBox {Margin=new Thickness(0,5,0,10)};
        if(kind is "received" or "sent") {
            foreach(var span in spans)period.Items.Add(new ComboBoxItem{Content=span.Label});
            period.SelectedIndex=2;
            form.Children.Add(Txt("Período",13,Theme.Muted,top:12));form.Children.Add(period);
            form.Children.Add(Txt("Data inicial",13,Theme.Muted));form.Children.Add(dateFrom);
            form.Children.Add(Txt("Data final (inclusiva)",13,Theme.Muted));form.Children.Add(dateTo);
            var periodNote=Txt("",12,Theme.Muted);periodNote.TextWrapping=TextWrapping.Wrap;
            form.Children.Add(periodNote);
            void ApplyPeriod() {
                var today=DateTime.Today;
                var label=(period.SelectedItem as ComboBoxItem)?.Content as string??"";
                switch(label) {
                    case "Hoje": dateFrom.SelectedDate=today;dateTo.SelectedDate=today;break;
                    case "Ontem": dateFrom.SelectedDate=today.AddDays(-1);dateTo.SelectedDate=today.AddDays(-1);break;
                    case "Últimos 7 dias": dateFrom.SelectedDate=today.AddDays(-6);dateTo.SelectedDate=today;break;
                    case "Esta semana": dateFrom.SelectedDate=today.AddDays(-(int)today.DayOfWeek);dateTo.SelectedDate=today;break;
                    case "Últimos 30 dias": dateFrom.SelectedDate=today.AddDays(-29);dateTo.SelectedDate=today;break;
                    case "Últimos 90 dias": dateFrom.SelectedDate=today.AddDays(-89);dateTo.SelectedDate=today;break;
                }
            }
            // Datas continuam editáveis: desabilitar deixava o campo cinza-claro no tema escuro. Mexer numa
            // delas passa o período para Personalizado, em vez de brigar com a escolha do menu.
            bool applying=false;
            void ToCustom(object? sender,EventArgs e) {
                if(applying)return;
                var custom=period.Items.Cast<ComboBoxItem>().First(i=>(string)i.Content=="Personalizado");
                if(!ReferenceEquals(period.SelectedItem,custom))period.SelectedItem=custom;
            }
            dateFrom.SelectedDateChanged+=(s,e)=>ToCustom(s,e);
            dateTo.SelectedDateChanged+=(s,e)=>ToCustom(s,e);
            refreshPeriod=()=>{
                // Qualquer lado por domínio encurta a janela: a varredura é a mesma, esteja o domínio no lado
                // que vai para a API ou no que é peneirado aqui.
                bool byDomain=DomainSide(originSide)||DomainSide(destinationSide);
                var today=DateTime.Today;
                int limit=AdvancedService.MaxDays(byDomain);
                for(int i=0;i<period.Items.Count;i++)
                    ((ComboBoxItem)period.Items[i]).IsEnabled=spans[i].Days<=limit;
                if(period.SelectedIndex>=0 && !((ComboBoxItem)period.Items[period.SelectedIndex]).IsEnabled)
                    period.SelectedIndex=2;
                dateFrom.DisplayDateStart=today.AddDays(1-limit);
                dateFrom.DisplayDateEnd=today;dateTo.DisplayDateStart=today.AddDays(1-limit);dateTo.DisplayDateEnd=today;
                periodNote.Text=byDomain
                    ?"Com domínio em qualquer um dos lados a API só sustenta 7 dias, então os períodos maiores ficam indisponíveis."
                    :"Com contas de e-mail nos dois lados, o período pode chegar a 90 dias.";
                applying=true;ApplyPeriod();applying=false;
            };
            period.SelectionChanged+=(s,e)=>{applying=true;ApplyPeriod();applying=false;};
            refreshPeriod();
        }
        var newMail=kind=="replace"?InputField(form,"Novo endereço",hint:"ana.silva@empresa.com.br"):new TextBox();
        var password=new PasswordBox {Margin=new Thickness(0,5,0,0)};
        var plainPassword=new TextBox {Margin=new Thickness(0,5,0,0),Visibility=Visibility.Collapsed};
        var keep=new ComboBox {ItemsSource=new[]{"Sim","Não"},SelectedIndex=0,Margin=new Thickness(0,5,0,12)};
        // A senha nova precisa ser conferida e copiada por quem vai entregá-la, então dá para exibi-la.
        string Secret()=>plainPassword.Visibility==Visibility.Visible?plainPassword.Text:password.Password;
        void ClearSecret(){password.Clear();plainPassword.Clear();}
        var passwordFeedback=Txt("",12,Theme.Muted);
        passwordFeedback.Name="PasswordFeedback";
        Button? execute=null;
        void ValidatePassword() {
            if(kind!="replace")return;
            var secret=Secret();var error=PasswordRules.ReplacementError(secret,accounts.Text,newMail.Text);bool valid=error==null;
            passwordFeedback.Text=(valid?"Senha válida. ":"Senha incompleta. ")+secret.Length+"/8 caracteres (mínimo); "+
                PasswordRules.TypeCount(secret)+"/3 tipos (mínimo).\n"+(error??"Sem sequências e sem nomes da conta ou domínio.");
            passwordFeedback.Foreground=valid?Theme.Ok:Theme.Muted;
            if(execute!=null)execute.IsEnabled=valid;
        }
        if(kind=="replace") {
            form.Children.Add(Txt("Nova senha",13,Theme.Muted));
            form.Children.Add(password);form.Children.Add(plainPassword);
            passwordFeedback.Margin=new Thickness(0,6,0,0);
            form.Children.Add(passwordFeedback);
            password.PasswordChanged+=(s,e)=>ValidatePassword();
            plainPassword.TextChanged+=(s,e)=>ValidatePassword();
            accounts.TextChanged+=(s,e)=>ValidatePassword();
            newMail.TextChanged+=(s,e)=>ValidatePassword();
            var revealRow=new WrapPanel{Margin=new Thickness(0,6,0,12)};
            var reveal=Btn("Mostrar senha",()=>{});
            revealRow.Children.Add(reveal);
            revealRow.Children.Add(Btn("Gerar senha",()=>{
                var generated=PasswordRules.Generate(accounts.Text,newMail.Text);
                password.Password=generated;plainPassword.Text=generated;
                ValidatePassword();
            }));
            reveal.Click+=(s,e)=>{
                bool showing=plainPassword.Visibility==Visibility.Visible;
                if(showing){password.Password=plainPassword.Text;plainPassword.Visibility=Visibility.Collapsed;password.Visibility=Visibility.Visible;}
                else {plainPassword.Text=password.Password;password.Visibility=Visibility.Collapsed;plainPassword.Visibility=Visibility.Visible;}
                reveal.Content=showing?"Mostrar senha":"Ocultar senha";
                ValidatePassword();
            };
            form.Children.Add(revealRow);
            form.Children.Add(Txt("Manter endereço antigo como apelido?",13,Theme.Muted));form.Children.Add(keep);
        }
        var job=Job(kind);
        bool showCurrent=job.Running || ReferenceEquals(processDetails,job);
        processDetails=null;
        // Faixa de erro: antes a mensagem saía pequena e cinza no meio da tela, e passava despercebida.
        var alertText=Txt("",14,Theme.Danger);
        alertText.TextWrapping=TextWrapping.Wrap;
        var alertRow=new StackPanel{Orientation=Orientation.Horizontal};
        var alertGlyph=new TextBlock{Text="",FontFamily=new FontFamily(Ui.Icons),FontSize=16,
            Foreground=Theme.Danger,Margin=new Thickness(0,1,12,0),VerticalAlignment=VerticalAlignment.Top};
        alertRow.Children.Add(alertGlyph);alertRow.Children.Add(alertText);
        var alert=new Border{Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,14),
            CornerRadius=new CornerRadius(10),Background=Theme.DangerSoft,BorderBrush=Theme.Danger,
            BorderThickness=new Thickness(1),Padding=new Thickness(16,13,16,13),Child=alertRow};
        Control? flagged=null;
        void ClearAlert() {
            alert.Visibility=Visibility.Collapsed;
            if(flagged!=null){flagged.BorderBrush=Theme.Line;flagged.BorderThickness=new Thickness(1);flagged=null;}
        }
        void ShowAlert(string message,Control? field) {
            alertText.Text=message;alert.Visibility=Visibility.Visible;
            if(field==null)return;
            flagged=field;field.BorderBrush=Theme.Danger;field.BorderThickness=new Thickness(1.6);
            field.Focus();field.BringIntoView();
        }
        Border? feedbackCard=null;
        var progressText=Txt(job.Status,14,Theme.Muted);
        var progress=new ProgressBar {Minimum=0,Maximum=job.Maximum,Value=job.Progress,Height=10,Margin=new Thickness(0,8,0,12)};
        void Status(string text){job.Status=text;progressText.Text=text;job.Report();}
        void Advance(double value){job.Progress=value;progress.Value=value;}
        var stop=Btn("Parar após a chamada atual",()=>{job.StopRequested=true;job.Report();});
        string[] pendingAccounts=Array.Empty<string>();
        // Result rows are tracked by account here: an "Ignorada — duplicada" row carries the same address and
        // would otherwise hide the "Não enviada" record of the account that was accepted but never processed.
        var processedAccounts=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output=new StackPanel(); output.Children.Add(progressText);output.Children.Add(progress);
        var rows=job.Rows;var messages=job.Messages;
        // Grupos e apelidos não dizem nada sobre upgrade: a tela mostra quota, uso e o produto sugerido.
        var resultGrid=kind=="upgrade"
            ?Table(new[]{("Conta","Account"),("Quota","Quota"),("Uso","Usage"),
                ("Produto atual","Product"),("Produto sugerido","Suggested"),("Detalhes","Details")})
            :Table(new[]{(kind is "received" or "sent"?"Busca":"Conta","Account"),("Resultado","Result"),("Detalhes","Details")});
        resultGrid.ItemsSource=rows;
        if(kind=="upgrade")resultGrid.SelectionMode=DataGridSelectionMode.Extended;
        output.Children.Add(resultGrid);
        if(kind is "received" or "sent") {
            var grid=Table(new[]{("Data","Date"),("Remetente","Sender"),("Destinatário","Recipient"),("Assunto","Subject"),("Tamanho","Size"),("Status","Status")});
            grid.ItemsSource=messages;output.Children.Add(grid);
        }

        void Export(string module,string[] headers,IEnumerable<string[]> values) {
            var save=new SaveFileDialog{Filter="CSV UTF-8|*.csv",FileName=Exports.FileName(module)};
            if(save.ShowDialog(this)==true)TrySave(save.FileName,Exports.CsvText(headers,values));
        }
        var exportResults=Btn("Exportar resultados CSV",()=>Export(kind,
            kind=="upgrade"
                ?new[]{"Conta","Quota","Uso","Produto atual","Produto sugerido","Detalhes"}
                :new[]{kind is "received" or "sent"?"Busca":"Conta","Resultado","Detalhes"},
            rows.Select(r=>kind=="upgrade"
                ?new[]{r.Account,r.Quota,r.Usage,r.Product,r.Suggested,r.Details}
                :new[]{r.Account,r.Result,r.Details})));
        exportResults.Margin=new Thickness(exportResults.Margin.Left,12,exportResults.Margin.Right,exportResults.Margin.Bottom);
        output.Children.Add(exportResults);
        if(kind is "received" or "sent")output.Children.Add(Btn("Exportar mensagens CSV",()=>Export(kind+"_mensagens",
            new[]{"Data","Remetente","Destinatário","Assunto","Tamanho","Status"},messages.Select(r=>new[]{r.Date,r.Sender,r.Recipient,r.Subject,r.Size,r.Status}))));
        if(kind=="upgrade")output.Children.Add(Btn("Enviar para Alteração de Licenças",()=>{
            var selected=resultGrid.SelectedItems.Cast<OperationRow>().Where(r=>r.Suggested.Length>0).ToArray();
            if(selected.Length==0){MessageBox.Show(this,"Selecione as contas com produto sugerido.","SkyAPI");return;}
            // Produto fora do painel do cliente não tem para onde ser enviado: a lista de destino da alteração
            // só traz o que está contratado, e o lote pararia na conferência sem dizer o porquê.
            var outside=selected.Where(r=>!r.SuggestionInPanel).Select(r=>r.Suggested).Distinct().ToArray();
            if(outside.Length>0){MessageBox.Show(this,"Ainda não dá para enviar: "+string.Join(", ",outside)+
                " não está contratado no painel do cliente.\n\nAdicione o produto no painel Skymail — o link "+
                "\"Como adicionar um produto no painel\" está logo acima — e rode a análise de novo.","SkyAPI");return;}
            var suggestions=selected.Select(r=>r.Suggested).Distinct().ToArray();
            if(suggestions.Length!=1){MessageBox.Show(this,"Selecione contas com o mesmo produto sugerido para preparar um lote.","SkyAPI");return;}
            Advanced("licenses",string.Join("\r\n",selected.Select(r=>r.Account)),domain.Text.Trim(),suggestions[0]);
        }));
        // A página só reflete a tarefa: ela pode estar rodando desde antes desta tela existir.
        void Sync() {
            stop.IsEnabled=job.Running && !job.StopRequested;
            stop.Visibility=job.Running?Visibility.Visible:Visibility.Collapsed;
            progress.Visibility=job.Running?Visibility.Visible:Visibility.Collapsed;
            output.Visibility=showCurrent && (rows.Count>0 || messages.Count>0)?Visibility.Visible:Visibility.Collapsed;
            bool attention=job.Failed || job.StopRequested || job.Status.Contains("interrompida") || job.Status.Contains("cancelada") ||
                rows.Any(r=>r.Result is "Falhou" or "Parcial" or "Pendente" or "Indeterminado" or "Não enviada" or "Inválida" or "Bloqueada");
            progressText.Foreground=job.Running?Theme.Muted:attention?Theme.Warn:Theme.Ok;
            if(feedbackCard!=null) {
                feedbackCard.Visibility=showCurrent?Visibility.Visible:Visibility.Collapsed;
                feedbackCard.Background=job.Running?Theme.Surface:attention?Theme.WarnSoft:Theme.OkSoft;
                feedbackCard.BorderBrush=job.Running?Theme.Line:attention?Theme.Warn:Theme.Ok;
            }
            form.IsEnabled=!job.Running;
            progressText.Text=job.Status;
            progress.Maximum=job.Maximum;progress.Value=job.Progress;
        }
        job.Changed+=Sync;
        pageCleanup=()=>{job.Changed-=Sync;resultGrid.ItemsSource=null;};
        Sync();
        async Task Guard(Func<AdvancedService,Task> action) {
            if(job.Running){MessageBox.Show(this,"Este módulo já tem uma operação em andamento. Acompanhe em Processos.","SkyAPI");return;}
            if(busy){MessageBox.Show(this,"Aguarde a operação em lote da tela anterior terminar.","SkyAPI");return;}
            if(demo || !api.HasToken){MessageBox.Show(this,demo?"Não é possível demonstrar este módulo. Ele consulta e altera dados reais na API. Saia da demonstração e conecte-se à API para usá-lo.":"Configure a conexão antes de executar.","SkyAPI");return;}
            showCurrent=true;job.Failed=false;job.Progress=0;job.Maximum=1;job.Status="Preparando operação…";
            job.Running=true;job.StopRequested=false;job.Started=DateTime.Now;
            connectButton.IsEnabled=false;UpdateJobsBadge();job.Report();
            pendingAccounts=Array.Empty<string>();processedAccounts.Clear();
            ClearAlert();
            // Erro encerra a operação: a barra volta a zero em vez de ficar parada na metade, como se ainda
            // houvesse alguma coisa andando.
            try {await action(new AdvancedService(api));}
            catch(ApiFailure ex){job.Failed=true;Advance(0);Status(ex.Message);ShowAlert(ex.Message,null);}
            catch(Exception ex) when(ex is InvalidOperationException or IOException or UnauthorizedAccessException){
                job.Failed=true;
                var message=ex is InvalidOperationException?ex.Message:"Não foi possível gravar o log. A operação foi interrompida.";
                Advance(0);Status(message);ShowAlert(message,(ex as FieldError)?.Field);
            }
            finally {
                foreach(var account in pendingAccounts.Where(a=>!processedAccounts.Contains(a)))rows.Add(new(account,"Não enviada","Operação interrompida antes deste registro."));
                job.Running=false;job.Finished=DateTime.Now;
                connectButton.IsEnabled=!busy && !AnyJobRunning;
                UpdateJobsBadge();job.Report();ClearSecret();
            }
        }
        if(kind=="licenses") {
            var consult=Btn("1. Consultar produtos do cliente",()=>_ = Guard(async service=>{
                rows.Clear();messages.Clear();
                products.ItemsSource=null;products.IsEnabled=false;clientCatalog=Array.Empty<ClientLicenseProduct>();
                Status("Consultando produtos…");Advance(0);
                clientCatalog=await service.ClientProducts();
                products.ItemsSource=clientCatalog;
                products.SelectedItem=clientCatalog.Count(p=>p.Product.Name==presetProduct)==1?clientCatalog.First(p=>p.Product.Name==presetProduct):null;
                products.IsEnabled=clientCatalog.Count>0;
                Advance(1);Status(clientCatalog.Count+" produtos de e-mail retornados pela API."+
                    (clientCatalog.Count>0?" Selecione o produto de destino.":""));
            }));
            // Consulta e destino precedem a entrada de contas.
            var productStart=form.Children.IndexOf(products)-1;
            var productControls=form.Children.Cast<UIElement>().Skip(productStart).Take(4).ToArray();
            foreach(var control in productControls)form.Children.Remove(control);
            form.Children.Insert(1,consult);
            for(int i=0;i<productControls.Length;i++)form.Children.Insert(2+i,productControls[i]);
        }
        execute=Btn("Conferir e executar",()=>_ = Guard(async service=>{
            rows.Clear();messages.Clear();Advance(0);
            string chosenDomain=domain.Text.Trim();
            var from=dateFrom.SelectedDate??DateTime.MinValue;
            var to=dateTo.SelectedDate?.Date.AddDays(1).AddSeconds(-1)??DateTime.MinValue;
            if(to>DateTime.Now && dateTo.SelectedDate?.Date==DateTime.Today)to=DateTime.Now;
            if(kind is "received" or "sent") {
                AdvancedService.ValidateDates(from,to,DateTime.Now);
                string[] Side((Func<bool> IsDomain,TextBox Box) side,string label) {
                    var values=side.Box.Text.Split(new[]{'\r','\n',',',';','\t',' '},
                        StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if(side.IsDomain()) {
                        if(values.Length>1)throw Fail(label+": com a opção Domínio, informe apenas um domínio.",side.Box);
                        if(values.Length==1&&!Planner.IsDomain(values[0]))
                            throw Fail(label+": \""+values[0]+"\" não é um domínio. Use empresa.com.br, ou troque a opção para Contas de e-mail.",side.Box);
                    } else foreach(var v in values)
                        if(!Planner.IsEmail(v))throw Fail(label+": \""+v+"\" não é um endereço de e-mail válido.",side.Box);
                    return values;
                }
                var origins=Side(originSide,"Remetente");
                var destinations=Side(destinationSide,"Destinatário");
                if(kind=="sent"&&origins.Length==0)throw Fail("Informe o remetente: um domínio ou pelo menos uma conta.",originSide.Box);
                if(kind=="received"&&destinations.Length==0)throw Fail("Informe o destinatário: um domínio ou pelo menos uma conta.",destinationSide.Box);
                // Consulta com domínio varre muito mais dados: a API não sustenta janelas longas nesse caso,
                // esteja o domínio no lado que vai para a API ou no que é peneirado aqui.
                bool byDomain=(origins.Length>0&&originSide.IsDomain())||(destinations.Length>0&&destinationSide.IsDomain());
                if(byDomain&&(to-from).TotalDays>AdvancedService.DomainDays+0.01)
                    throw Fail(AdvancedService.DomainWindow,dateFrom);
                // Uma consulta por valor do lado que é seu; o outro lado inteiro é peneirado localmente.
                var mine=kind=="sent"?origins:destinations;
                var theirs=kind=="sent"?destinations:origins;
                string Describe(string value)=>kind=="sent"
                    ?value+" → "+(theirs.Length>0?string.Join(", ",theirs):"qualquer destinatário")
                    :(theirs.Length>0?string.Join(", ",theirs):"qualquer remetente")+" → "+value;
                var plan="Operação: "+AdvancedName(kind)+"\nPeríodo: "+from+" até "+to+
                    "\nConsultas à API: "+mine.Length+"\nFiltro aplicado no aplicativo: "+
                    (theirs.Length>0?string.Join(", ",theirs):"nenhum")+"\n\n"+
                    string.Join("\n",mine.Take(20).Select(Describe))+(mine.Length>20?"\n…":"");
                if(!ConfirmAdvanced(plan,"Consultar")){Status("Consulta cancelada antes da execução.");return;}
                job.Maximum=progress.Maximum=mine.Length;
                using var searchLog=NewLog(kind);
                int searched=0;
                foreach(var value in mine) {
                    if(job.StopRequested)break;
                    var watch=Stopwatch.StartNew();int before=messages.Count;
                    OperationRow row;
                    try {
                        var scan=await service.Messages(kind=="sent",value,theirs,from,to,messages.Add,t=>Status(t),()=>job.StopRequested);
                        int found=messages.Count-before;
                        // O total anunciado pela API muda enquanto a consulta roda; a diferença é informação,
                        // não falha, e o que veio continua na tela e no CSV.
                        row=new(Describe(value),job.StopRequested?"Parcial":"Sucesso",
                            (found==1?"1 mensagem encontrada.":found+" mensagens encontradas.")+
                            (scan.Truncated?" A API parou de entregar páginas antes do fim mesmo com o período "+
                                "dividido em fatias menores"+
                                (scan.Reported.HasValue?" ("+scan.Fetched+" de "+scan.Reported+" registros anunciados)":"")+
                                ". Consulte um período menor para conferir o que faltou.":""));
                    }
                    catch(ApiFailure ex){row=new(Describe(value),messages.Count>before?"Parcial":"Falhou",ex.Message);}
                    catch(InvalidOperationException ex){row=new(Describe(value),messages.Count>before?"Parcial":"Falhou",ex.Message);}
                    rows.Add(row);searchLog.Write(kind,value,AdvancedName(kind),row.Result,watch.Elapsed);
                    Advance(++searched);
                }
                Status((job.StopRequested?"Consulta interrompida. ":"Consulta concluída. ")+
                    (messages.Count==1?"1 mensagem encontrada.":messages.Count+" mensagens encontradas."));
                return;
            }
            if(kind=="replace") {
                var old=accounts.Text.Trim();var next=newMail.Text.Trim();
                if(!BatchInput.IsMailboxAddress(old))throw Fail("Conta atual: informe um endereço válido.",accounts);
                if(!BatchInput.IsMailboxAddress(next))throw Fail("Novo endereço: informe um endereço válido.",newMail);
                if(old.Equals(next,StringComparison.OrdinalIgnoreCase))throw Fail("O novo endereço precisa ser diferente da conta atual.",newMail);
                if(PasswordRules.ReplacementError(Secret(),old,next) is string passwordError)
                    throw Fail(passwordError,password.Visibility==Visibility.Visible?password:plainPassword);
                if(!ConfirmAdvanced("Conta atual: "+old+"\nNovo endereço: "+next+"\nManter apelido: "+keep.Text+"\nSenha: informada (oculta)\nAcompanhamento: até 11 minutos.","Executar")){Status("Operação cancelada antes da execução.");return;}
                using var log=NewLog(kind);var watch=Stopwatch.StartNew();
                OperationRow row;
                // Perder a conexão no meio da substituição não pode sumir com o registro: a renomeação já pode
                // ter sido enviada, e quem operou precisa saber em que ponto parou.
                try {
                    row=await service.Replace(old,next,Secret,keep.SelectedIndex==0,
                        text=>{Status(old+" → "+next+" · "+text);Advance(Math.Min(.95,watch.Elapsed.TotalSeconds/660));},()=>job.StopRequested);
                }catch(InvalidOperationException ex){
                    row=new(old,"Pendente","Operação interrompida: "+ex.Message+
                        " A renomeação pode já ter sido enviada. Consulte "+next+" e "+old+" antes de repetir.");
                }
                rows.Add(row);log.Write(kind,row.Account,"Substituição de colaborador",row.Result,watch.Elapsed);
                Advance(1);Status(row.Details);return;
            }
            if(kind=="upgrade" && !Planner.IsDomain(chosenDomain))
                throw Fail("Informe um domínio válido no campo Domínio, como empresa.com.br.",domain);
            // Guardados fora da conferência: a execução precisa saber o saldo e o cliente de cada domínio para
            // já contratar o que falta, em vez de falhar tudo e pedir de novo.
            var alreadyThere=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var groupClientOf=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var groupStock=new Dictionary<string,int?>(StringComparer.Ordinal);
            List<GroupInput> groups=new();List<GroupEditPlan> plans=new();AccountInput parsed;
            if(kind=="groups") {
                var g=BatchInput.Groups(accounts.Text);groups=g.Groups;
                parsed=new(groups.Select(x=>x.Email).ToArray(),g.Issues);
            }else if(kind=="groupedit") {
                var g=BatchInput.GroupEdits(accounts.Text);plans=g.Plans;
                parsed=new(plans.Select(p=>p.Email).ToArray(),g.Issues);
            }else if(kind=="upgrade" && accounts.Text.Trim().Length==0) {
                // Sem lista, o domínio inteiro: as caixas saem dos produtos do cliente, não de uma planilha.
                Status("Procurando as caixas do domínio…");
                var found=await service.Mailboxes(chosenDomain,t=>Status(t),()=>job.StopRequested);
                if(found.Count==0)throw new InvalidOperationException(
                    "A API não retornou caixas para este domínio. Confira o domínio ou informe as contas na lista.");
                parsed=new(found,Array.Empty<InputIssue>());
            }else parsed=BatchInput.Accounts(accounts.Text,kind=="upgrade"?chosenDomain:"");
            foreach(var issue in parsed.Issues)rows.Add(new(issue.Account,issue.Result,issue.Details));
            if(parsed.Issues.Any(i=>i.Result=="Inválida"))
                throw Fail("Há linhas inválidas na lista. Corrija as marcadas como Inválida na tabela abaixo e execute de novo. Primeira: "+
                    parsed.Issues.First(i=>i.Result=="Inválida").Details,accounts);
            job.Maximum=progress.Maximum=parsed.Accounts.Count;var checks=new List<LicenseCheck>();
            MailProduct? destination=null;int? available=null;string productLabel="";
            if(kind is "licenses" or "upgrade") {
                if(kind=="licenses"){
                    if(products.SelectedItem is not ClientLicenseProduct selected || !clientCatalog.Contains(selected))
                        throw Fail("Consulte os produtos do cliente e selecione o produto de destino antes de executar.",products);
                    destination=selected.Product;
                    foreach(var accountDomain in parsed.Accounts.Select(a=>a.Split('@')[1]).Distinct(StringComparer.OrdinalIgnoreCase)) {
                        if(job.StopRequested)throw Fail("Conferência interrompida; nenhuma alteração enviada.");
                        if(await service.ClientForDomain(accountDomain)!=selected.ClientId)
                            throw Fail("O domínio "+accountDomain+" não pertence ao cliente do produto selecionado. Separe as contas por cliente.",accounts);
                    }
                    int index=0;
                    foreach(var account in parsed.Accounts) {
                        if(job.StopRequested)throw new InvalidOperationException("Conferência interrompida; nenhuma alteração enviada.");
                        Status("Validando licenças… "+(++index)+"/"+parsed.Accounts.Count);
                        try {checks.Add(LicenseRules.Check(await service.Mailbox(account),destination));}
                        catch(ApiFailure ex){if(ex.Reply.Stop)throw;checks.Add(new(account,"",destination.Name,"Falhou",ex.Message));}
                        catch(InvalidOperationException ex){checks.Add(new(account,"",destination.Name,"Falhou",ex.Message));}
                        Advance(index);
                    }
                    foreach(var check in checks)rows.Add(new(check.Account,check.Result,check.Details));
                    var destinationStock=await service.ClientProductBalance(selected.ClientId,destination);
                    available=destinationStock?.Available;
                    Status("Confirmando o nome aceito pela API…");
                    productLabel=await service.LabelFromStock(destinationStock,destination);
                }else catalog=await service.Products(chosenDomain);
            }
            decimal percent=0;
            if(kind=="upgrade" && (!decimal.TryParse(threshold.Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out percent)||percent<=0||percent>100))
                throw Fail("Uso mínimo: informe um percentual maior que 0 e até 100.",threshold);
            int required=kind=="licenses"?checks.Count(c=>c.Eligible):parsed.Accounts.Count;
            int purchaseLimit=kind=="licenses"?Math.Max(0,required-(available??0)):0;
            string summary0=kind=="groupedit"
                ?string.Join("\n\n",plans.Select(p=>p.Email+" ("+p.Changes.Count+")\n   "+string.Join("\n   ",p.Lines())))
                :"";
            string summary="Domínio: "+(chosenDomain.Length>0?chosenDomain:string.Join(", ",parsed.Accounts.Select(a=>a.Split('@')[1]).Distinct()))+
                "\nOperação: "+AdvancedName(kind)+"\nContas válidas: "+parsed.Accounts.Count+"\nIgnoradas: "+
                (parsed.Issues.Count+checks.Count(c=>!c.Eligible));
            if(destination!=null)summary+="\nDestino: "+destination.Name+
                "\nNome enviado à API: "+(productLabel.Length>0?productLabel:destination.Name+
                    " (nome do catálogo; nenhuma caixa usa esse produto para confirmar o nome aceito)")+
                "\nLicenças necessárias: "+required+
                "\nLicenças disponíveis: "+(available?.ToString()??"Não informadas pela API")+"\nFaltando: "+(available.HasValue?Math.Max(0,required-available.Value).ToString():"A confirmar pela API");
            if(kind=="groups") {
                // Existing groups consume no licence; a group whose existence could not be read still counts.
                int unconfirmed=0,inspected=0;
                foreach(var group in groups) {
                    if(job.StopRequested)throw new InvalidOperationException("Conferência interrompida; nenhum grupo criado.");
                    Status("Conferindo grupos existentes… "+(++inspected)+"/"+groups.Count);
                    bool? there;
                    try {there=await service.GroupExists(group.Email);}
                    catch(ApiFailure ex){if(ex.Reply.Stop)throw;there=null;}
                    catch(InvalidOperationException){there=null;}
                    if(there==true)alreadyThere.Add(group.Email);else if(there==null)unconfirmed++;
                }
                int needed=groups.Count-alreadyThere.Count;
                summary+="\nGrupos informados: "+groups.Count+"\nJá existentes: "+alreadyThere.Count+
                    "\nLicenças de Grupo necessárias: "+needed;
                if(unconfirmed>0)summary+=" (inclui "+unconfirmed+" com existência não confirmada pela API)";
                // Stock belongs to the client, so domains of the same client share one balance and are counted once.
                var perClient=new Dictionary<string,(int? Available,SortedSet<string> Domains,int Needed)>(StringComparer.Ordinal);
                var withoutClient=new List<string>();
                foreach(var batchDomain in groups.GroupBy(g=>g.Email.Split('@')[1],StringComparer.OrdinalIgnoreCase)) {
                    if(job.StopRequested)throw new InvalidOperationException("Conferência interrompida; nenhum grupo criado.");
                    Status("Consultando licenças de Grupo… "+batchDomain.Key);
                    int need=batchDomain.Count(g=>!alreadyThere.Contains(g.Email));
                    GroupStock? stock;
                    try {stock=await service.GroupBalance(batchDomain.Key);}
                    catch(ApiFailure ex){if(ex.Reply.Stop)throw;stock=null;}
                    if(stock==null){withoutClient.Add(batchDomain.Key+": necessárias "+need+"; cliente não identificado pela API");continue;}
                    groupClientOf[batchDomain.Key]=stock.ClientId;
                    if(perClient.TryGetValue(stock.ClientId,out var acc)) {
                        acc.Domains.Add(batchDomain.Key);
                        perClient[stock.ClientId]=(acc.Available??stock.Available,acc.Domains,acc.Needed+need);
                    } else perClient[stock.ClientId]=(stock.Available,new SortedSet<string>(StringComparer.OrdinalIgnoreCase){batchDomain.Key},need);
                }
                foreach(var client in perClient)
                    summary+="\nCliente "+client.Key+" ("+string.Join(", ",client.Value.Domains)+"): necessárias "+client.Value.Needed+
                        "; disponíveis "+(client.Value.Available?.ToString()??"não informadas")+
                        "; faltando "+(client.Value.Available.HasValue?Math.Max(0,client.Value.Needed-client.Value.Available.Value).ToString():"a confirmar");
                foreach(var line in withoutClient)summary+="\n"+line;
                int shortfall=perClient.Sum(c=>Math.Max(0,c.Value.Needed-(c.Value.Available??0)));
                purchaseLimit=shortfall+groups.Count(g=>!alreadyThere.Contains(g.Email) && !groupClientOf.ContainsKey(g.Email.Split('@')[1]));
                summary+="\nO saldo é compartilhado por cliente e contado uma única vez.";
                if(shortfall>0)summary+="\n\nFALTAM "+shortfall+" LICENÇA"+(shortfall==1?"":"S")+
                    ". Ao executar, "+(shortfall==1?"ela será contratada":"elas serão contratadas")+
                    " automaticamente, uma por grupo, e o valor entra na sua conta Skymail.";
                foreach(var client in perClient)groupStock[client.Key]=client.Value.Available;
            }
            if(kind=="upgrade")summary+="\nUso mínimo: "+percent+"%"+(accounts.Text.Trim().Length==0?" · caixas encontradas pela API":" · lista informada");
            if(kind=="groupedit")summary+="\nAlterações:\n"+summary0;
            if(purchaseLimit>0)summary+="\n\nDeseja autorizar a contratação de até "+purchaseLimit+
                " licenças adicionais e executar? A API contrata durante cada alteração/criação, conforme a necessidade, com cobrança na conta Skymail."+
                (summary.Contains("não informad",StringComparison.OrdinalIgnoreCase)||summary.Contains("Não informad",StringComparison.OrdinalIgnoreCase)||summary.Contains("não identificado")?"\nA API não informou a quantidade de licenças livres para parte do lote; o limite considera uma por registro elegível.":"")+"\nNenhuma contratação além desse limite será enviada sem nova confirmação.";
            if(!ConfirmAdvanced(summary,purchaseLimit>0?"Contratar até "+purchaseLimit+" licenças e executar":"Executar")){Status("Operação cancelada antes da execução.");return;}
            pendingAccounts=parsed.Accounts.ToArray();
            rows.Clear();foreach(var issue in parsed.Issues)rows.Add(new(issue.Account,issue.Result,issue.Details));
            Advance(0);using var operationLog=NewLog(kind);
            int? licenseStock=available;
            bool halted=false;int done=0,contracted=0;
            foreach(var account in parsed.Accounts) {
                var watch=Stopwatch.StartNew();OperationRow row;
                int previousMessages=messages.Count;
                Status(AdvancedName(kind)+" "+(done+1)+"/"+parsed.Accounts.Count+" · "+account);
                if(halted||job.StopRequested)row=new(account,"Não enviada","Lote interrompido antes desta conta.");
                else try {
                    if(kind=="licenses") {
                        var check=checks.Single(c=>c.Account==account);
                        if(!check.Eligible)row=new(account,check.Result,check.Details);
                        else {
                            bool buy=licenseStock.GetValueOrDefault()<=0 && purchaseLimit>0;
                            var result=await service.ChangeLicense(check,destination!,buy,productLabel,()=>{contracted++;purchaseLimit--;});
                            if(result.NeedsLicense && !buy && !result.Stop && !job.StopRequested) {
                                if(ConfirmAdvanced("O saldo mudou desde a conferência. A API recusou esta alteração por falta de licença. Autoriza contratar até 1 licença adicional para "+account+"?","Contratar 1 licença e continuar"))
                                    result=await service.ChangeLicense(check,destination!,true,productLabel,()=>contracted++);
                                else halted=true;
                            }
                            if(!buy && result.Row.Result=="Sucesso" && licenseStock>0)licenseStock--;
                            row=result.Row;halted=halted||result.Stop||result.NeedsLicense;
                        }
                    }else if(kind=="groupedit") {
                        var result=await service.EditGroup(plans.Single(p=>p.Email==account));
                        row=result.Row;halted=result.Stop;
                    }else if(kind=="groups") {
                        var group=groups.Single(g=>g.Email==account);
                        // Saldo conhecido e esgotado: já vai com a confirmação de compra que a conferência avisou.
                        bool buy=false;string stockClient="";int? left=null;
                        if(!alreadyThere.Contains(group.Email)) {
                            if(groupClientOf.TryGetValue(group.Email.Split('@')[1],out var client)) {
                                stockClient=client;groupStock.TryGetValue(client,out left);
                            }
                            buy=left.GetValueOrDefault()<=0 && purchaseLimit>0;
                        }
                        var result=await service.CreateGroup(group,buy,()=>{contracted++;purchaseLimit--;});
                        if(result.NeedsLicense && !buy && !result.Stop && !job.StopRequested) {
                            if(ConfirmAdvanced("O saldo mudou desde a conferência. Autoriza contratar até 1 licença adicional de Grupo para "+account+"?","Contratar 1 licença e continuar"))
                                result=await service.CreateGroup(group,true,()=>contracted++);
                            else halted=true;
                        }
                        if(!buy && result.Row.Result=="Sucesso" && left>0)groupStock[stockClient]=left.Value-1;
                        row=result.Row;halted=halted||result.Stop||result.NeedsLicense;
                    }else if(kind=="upgrade") {
                        var box=await service.Mailbox(account);
                        var plan=kind=="upgrade" && box.Percent>=percent?LicenseRules.Plan(box,catalog):null;
                        // Produto sugerido fora do painel não é "nenhum upgrade disponível": é upgrade que
                        // existe na Skymail e depende de o cliente adicionar o produto no painel.
                        var detail=kind=="upgrade"
                            ?(box.Percent==null?"Quota ou uso não informado."
                             :box.Percent<percent?"Abaixo do limite selecionado."
                             :plan==null?"Nenhum upgrade compatível na família "+(LicenseRules.IsExchange(box.Product)?"SkyExchange":"SkyMail")+" disponível."
                             :plan.InPanel?"Upgrade sugerido; nenhuma alteração realizada."
                             :"Upgrade sugerido, mas "+plan.Product.Name+" não está contratado no painel do cliente. "+
                              "Adicione o produto no painel Skymail e consulte de novo para poder enviar a alteração.")
                            :"Consulta concluída.";
                        row=new(account,"Sucesso",detail){Product=box.Product,Suggested=plan?.Product.Name??"",
                            SuggestionInPanel=plan?.InPanel??true,
                            Quota=Display.Gigabytes(box.Quota),
                            Usage=Display.Usage(box.Used,box.Percent)};
                    }else row=new(account,"Ignorada","Operação sem tratamento nesta versão.");
                }catch(ApiFailure ex){row=new(account,messages.Count>previousMessages?"Parcial":"Falhou",ex.Message);halted=ex.Reply.Stop;}
                catch(InvalidOperationException ex){row=new(account,messages.Count>previousMessages?"Parcial":"Falhou",ex.Message);}
                rows.Add(row);processedAccounts.Add(account);operationLog.Write(kind,account,AdvancedName(kind),row.Result,watch.Elapsed);
                Advance(++done);
            }
            Status((job.StopRequested||halted?"Operação interrompida.":"Operação concluída.")+
                (contracted>0?" "+contracted+" confirmação"+(contracted==1?"":"ões")+" de compra enviada"+(contracted==1?"":"s")+" à API.":"")+
                " Confira os resultados e exporte o CSV.");
        }),true);
        form.Children.Add(execute);
        ValidatePassword();
        output.Children.Insert(2,stop);
        output.Children.Remove(progressText);output.Children.Remove(progress);output.Children.Remove(stop);
        var feedback=new StackPanel();feedback.Children.Add(progressText);feedback.Children.Add(progress);feedback.Children.Add(stop);
        feedbackCard=Panel(feedback);
        page.Children.Add(alert);page.Children.Add(feedbackCard);page.Children.Add(Panel(form));page.Children.Add(output);
        Sync();
        render=()=>{if(showCurrent)processDetails=job;Advanced(kind,preset,presetDomain,presetProduct);};
    }
    private OperationLog NewLog(string module)=>new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Skynova","SkyAPI","Logs"),module);
    private bool ConfirmAdvanced(string text,string accept) {
        var dialog=new Window{Owner=this,Title="Conferência — SkyAPI",Width=580,SizeToContent=SizeToContent.Height,
            MaxHeight=750,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Theme.Bg,ResizeMode=ResizeMode.NoResize};
        Ui.Install(dialog);
        var content=new StackPanel{Margin=new Thickness(24)};
        content.Children.Add(Txt(text,14,Theme.Ink));
        var actions=new WrapPanel{Margin=new Thickness(0,20,0,0)};
        actions.Children.Add(Btn("Cancelar",()=>dialog.DialogResult=false));
        actions.Children.Add(Btn(accept,()=>dialog.DialogResult=true,true));content.Children.Add(actions);
        dialog.Content=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        return dialog.ShowDialog()==true;
    }
}
