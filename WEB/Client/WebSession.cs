using SkyAPI.Core;
using Microsoft.JSInterop;
namespace SkyAPI.Web;

public sealed class WebJob(string kind,string name) {
    public string Id {get;}=Guid.NewGuid().ToString("N");
    public string Kind=kind,Name=name,Status="Preparando conferência…";
    public bool Running=true,Stop,Attention;
    public int Progress,Total=1;
    public DateTime Started=DateTime.Now,Finished;
    public List<OperationRow> Rows {get;}=new();
    public List<MessageRow> Messages {get;}=new();
    public List<string> Log {get;}=new(){"data_hora;conta;resultado;detalhes"};
}
public sealed record ConfirmModel(string Title,string Text,string Accept,bool Destructive,bool RequireExecutar=false);
public sealed class WebSession : IDisposable {
    public ApiClient Api {get;}
    public AdvancedService Service {get;}
    public List<WebJob> Jobs {get;}=new();
    public bool Demo,Connecting;
    public string Error="",RateMessage="";
    public ConfirmModel? Dialog;
    public event Action? Changed;
    private TaskCompletionSource<bool>? confirmation;
    private readonly SemaphoreSlim dialogGate=new(1,1);
    private readonly IJSRuntime js;
    public bool Running=>Jobs.Any(j=>j.Running);
    public WebSession(string origin,IJSRuntime js) {
        this.js=js;Api=new(new BrowserTransport(origin));Service=new(Api);
        Api.RateLimitWaiting+=until=>{RateMessage=until.HasValue?"Aguardando o limite da API renovar às "+until.Value.ToLocalTime().ToString("HH:mm:ss")+". A operação continuará automaticamente.":"";Notify();};
    }
    public void Notify()=>Changed?.Invoke();
    public async Task Connect(string username,string password,string key,string token) {
        if(Running)throw new InvalidOperationException("Aguarde os processos antes de trocar a conexão.");
        Connecting=true;Error="";Notify();
        try {
            if(!await js.InvokeAsync<bool>("sky.lock"))throw new InvalidOperationException("Já existe uma conexão SkyAPI em outra aba. Use essa aba ou desconecte-a primeiro.");
            Api.ClearToken();Demo=false;
            if(token.Trim().Length>0)Api.SetToken(token);else await Api.LoginAsync(username,password,key);
        }catch {await js.InvokeVoidAsync("sky.unlock");throw;}
        finally{Connecting=false;Notify();}
    }
    public async Task Disconnect() {
        if(Running)throw new InvalidOperationException("Aguarde ou interrompa os processos antes de desconectar.");
        Api.ClearToken();Demo=false;await js.InvokeVoidAsync("sky.unlock");Notify();
    }
    public async Task<bool> Confirm(string title,string text,string accept="Executar",bool destructive=false,bool requireExecutar=false) {
        await dialogGate.WaitAsync();
        try {
            confirmation=new(TaskCreationOptions.RunContinuationsAsynchronously);Dialog=new(title,text,accept,destructive,requireExecutar);Notify();
            return await confirmation.Task;
        }finally{Dialog=null;confirmation=null;dialogGate.Release();Notify();}
    }
    public void Answer(bool accepted)=>confirmation?.TrySetResult(accepted);
    public async Task Run(string kind,string name,Func<WebJob,Task> action) {
        if(Jobs.Any(j=>j.Kind==kind&&j.Running)){Error="Este módulo já está em execução. Acompanhe em Processos.";Notify();return;}
        if(!Demo&&!Api.HasToken){Error="Configure a conexão antes de executar.";Notify();return;}
        Error="";var job=new WebJob(kind,name);Jobs.Insert(0,job);Notify();
        await js.InvokeVoidAsync("sky.busy",true);
        try {await action(job);if(job.Stop)job.Attention=true;}
        catch(Exception ex) when(ex is InvalidOperationException or IOException or FormatException) {
            job.Attention=true;job.Status=ex.Message;
            Add(job,new("Conferência","Falhou",ex.Message));
        }
        catch {job.Attention=true;job.Status="A operação foi interrompida. Confira os resultados antes de repetir.";}
        finally{job.Running=false;job.Finished=DateTime.Now;Notify();await js.InvokeVoidAsync("sky.busy",Running);}
    }
    public void Status(WebJob job,string text){job.Status=text;Notify();}
    public void Add(WebJob job,OperationRow row) {
        job.Rows.Add(row);
        job.Log.Add(string.Join(";",new[]{DateTime.Now.ToString("O"),row.Account,row.Result,row.Details}.Select(Csv.Cell)));
        if(row.Result is "Falhou" or "Erro" or "Parcial" or "Pendente" or "Indeterminado" or "Não enviada" or "Não enviado" or "Bloqueada" or "Inválida")job.Attention=true;
        Notify();
    }
    public void End(WebJob job)=>Status(job,job.Stop?"Operação interrompida. Confira os resultados.":job.Attention?"Operação encerrada com registros que exigem conferência.":"Operação concluída com sucesso.");
    /// <summary>Conferência antes de enviar. Nos lotes fora da demonstração o aplicativo exige que se
    /// digite EXECUTAR; os módulos avançados confirmam só no botão, como no Windows.</summary>
    public async Task<bool> Review(WebJob job,string summary,int purchase=0,bool destructive=false,bool requireExecutar=false) {
        if(job.Stop){job.Attention=true;Status(job,"Conferência interrompida. Nenhuma alteração enviada.");return false;}
        if(purchase>0)summary+="\n\nAutoriza contratar até "+purchase+" licenças adicionais durante a operação, com cobrança na conta Skymail? Nenhuma contratação além desse limite será enviada sem nova confirmação.";
        bool accepted=await Confirm("Conferência — "+job.Name,summary,
            purchase>0?"Contratar até "+purchase+" licenças e executar":requireExecutar?"Confirmar execução":Demo?"Simular":"Executar",
            destructive,requireExecutar);
        if(!accepted||job.Stop){job.Attention=true;Status(job,"Operação cancelada antes da execução.");return false;}
        return true;
    }
    public AccountInput Accounts(string text,string domain="") {
        var parsed=BatchInput.Accounts(text,domain);
        if(parsed.Issues.Any(i=>i.Result=="Inválida"))throw new InvalidOperationException(string.Join("\n",parsed.Issues.Where(i=>i.Result=="Inválida").Select(i=>i.Details)));
        return parsed;
    }
    public async Task Basic(WebJob job,Operation op,string text,string status,string password) {
        var plan=Planner.Build(op,text,status,password);
        if(plan.Errors.Count>0)throw new InvalidOperationException(string.Join("\n",plan.Errors));
        var items=plan.Items.ToArray();int purchase=0;string extra="";
        if(op==Operation.RestoreAccounts&&!Demo) {
            var restore=await Service.RestorationPurchases(items.Select(i=>i.Target));purchase=restore.PurchaseAccounts.Count;extra="\n"+restore.Summary;
            if(extra.Contains("não informadas"))extra+="\nA quantidade de licenças livres não foi informada; o limite considera uma por conta.";
            items=items.Select(i=>restore.PurchaseAccounts.Contains(i.Target)?i with {Fields=new Dictionary<string,string>(i.Fields){["confirm_purchase"]="true"}}:i).ToArray();
        }
        var summary="Registros: "+items.Length+"\n"+string.Join("\n",plan.Warnings)+extra+"\n\n"+string.Join("\n",items.Select(i=>i.Target+" — "+i.Description));
        if(!await Review(job,summary,purchase,op is Operation.DeleteAccounts or Operation.DeleteGroups or Operation.DeleteDns,!Demo))return;
        job.Total=items.Length;
        await BatchRunner.RunAsync(items,async item=> {
            Status(job,"Processando "+(job.Progress+1)+"/"+job.Total+" · "+item.Target);
            if(Demo){await Task.Delay(100);return new(item.Target,item.Description,"Simulado",null,"Demonstração: nenhuma chamada à API.");}
            return await Api.ExecuteAsync(item);
        },result=>{job.Progress++;Add(job,new(result.Target,result.Status,result.Message));return Task.CompletedTask;},()=>job.Stop,0);
        End(job);
    }
    public async Task Licenses(WebJob job,string input,ClientLicenseProduct selected) {
        var parsed=Accounts(input);var destination=selected.Product;var checks=new List<LicenseCheck>();
        job.Total=parsed.Accounts.Count;
        foreach(var domain in parsed.Accounts.Select(a=>a.Split('@')[1]).Distinct(StringComparer.OrdinalIgnoreCase))
            if(await Service.ClientForDomain(domain)!=selected.ClientId)throw new InvalidOperationException("O domínio "+domain+" não pertence ao cliente do produto selecionado.");
        foreach(var account in parsed.Accounts) {
            if(job.Stop){End(job);return;}
            Status(job,"Validando licenças… "+(++job.Progress)+"/"+job.Total);
            try {checks.Add(LicenseRules.Check(await Service.Mailbox(account),destination));}
            catch(ApiFailure ex){if(ex.Reply.Stop)throw;checks.Add(new(account,"",destination.Name,"Falhou",ex.Message));}
        }
        var stock=await Service.ClientProductBalance(selected.ClientId,destination);int? left=stock?.Available;
        var label=await Service.LabelFromStock(stock,destination);int needed=checks.Count(c=>c.Eligible),purchase=Math.Max(0,needed-(left??0));
        string summary="Cliente: "+selected.ClientName+"\nDestino: "+destination.Name+"\nContas: "+parsed.Accounts.Count+
            "\nIgnoradas/bloqueadas: "+checks.Count(c=>!c.Eligible)+"\nLicenças necessárias: "+needed+"\nLicenças disponíveis: "+(left?.ToString()??"Não informadas pela API")+
            "\nLicenças adicionais: "+purchase+(left==null?" (limite máximo; quantidade livre não informada)":"")+"\n\n"+
            string.Join("\n",checks.Select(c=>c.Account+" — "+c.Result+" — "+c.Details));
        if(!await Review(job,summary,purchase))return;
        foreach(var issue in parsed.Issues)Add(job,new(issue.Account,issue.Result,issue.Details));
        job.Progress=0;bool halted=false;
        foreach(var check in checks) {
            OperationRow row;
            if(job.Stop||halted)row=new(check.Account,"Não enviada","Lote interrompido antes desta conta.");
            else if(!check.Eligible)row=new(check.Account,check.Result,check.Details);
            else {
                Status(job,"Alterando licença · "+check.Account);
                try {
                    bool buy=left.GetValueOrDefault()<=0&&purchase>0;
                    var result=await Service.ChangeLicense(check,destination,buy,label,()=>purchase--);
                    if(result.NeedsLicense&&!buy&&!result.Stop&&!job.Stop) {
                        if(await Confirm("Saldo alterado","A API informou falta de licença para "+check.Account+". Autoriza contratar até 1 licença adicional?","Contratar 1 e continuar"))
                            result=await Service.ChangeLicense(check,destination,true,label);
                        else halted=true;
                    }
                    if(!buy&&result.Row.Result=="Sucesso"&&left>0)left--;
                    row=result.Row;halted|=result.Stop||result.NeedsLicense;
                }catch(ApiFailure ex){row=new(check.Account,"Falhou",ex.Message);halted=ex.Reply.Stop;}
                catch(InvalidOperationException ex){row=new(check.Account,"Falhou",ex.Message);}
            }
            job.Progress++;Add(job,row);
        }
        End(job);
    }
    public async Task Upgrade(WebJob job,string input,string domain,decimal threshold) {
        if(!Planner.IsDomain(domain))throw new InvalidOperationException("Informe um domínio válido.");
        if(threshold<=0||threshold>100)throw new InvalidOperationException("O uso mínimo deve estar entre 1 e 100%.");
        var catalog=await Service.Products(domain);
        var parsed=input.Trim().Length==0?new AccountInput(await Service.Mailboxes(domain,t=>Status(job,t),()=>job.Stop),Array.Empty<InputIssue>()):Accounts(input,domain);
        if(parsed.Accounts.Count==0)throw new InvalidOperationException("A API não retornou caixas para este domínio.");
        if(!await Review(job,"Domínio: "+domain+"\nContas: "+parsed.Accounts.Count+"\nUso mínimo: "+threshold+"%\nNenhuma alteração será realizada."))return;
        job.Total=parsed.Accounts.Count;
        foreach(var account in parsed.Accounts) {
            if(job.Stop)break;Status(job,"Analisando · "+account);
            try {
                var box=await Service.Mailbox(account);var plan=box.Percent>=threshold?LicenseRules.Plan(box,catalog):null;
                Add(job,new(account,"Sucesso",box.Percent==null?"Quota ou uso não informado.":box.Percent<threshold?"Abaixo do limite selecionado.":plan==null?"Nenhum upgrade compatível disponível.":plan.InPanel?"Upgrade sugerido; nenhuma alteração realizada.":"Produto sugerido ainda não contratado no painel.") {
                    Product=box.Product,Suggested=plan?.Product.Name??"",SuggestionInPanel=plan?.InPanel??true,Quota=Display.Gigabytes(box.Quota),Usage=Display.Usage(box.Used,box.Percent)});
            }catch(ApiFailure ex){Add(job,new(account,"Falhou",ex.Message));if(ex.Reply.Stop)break;}
            job.Progress++;Notify();
        }
        End(job);
    }
    public async Task Groups(WebJob job,string input,bool edit) {
        if(edit) {
            var parsed=BatchInput.GroupEdits(input);
            if(parsed.Issues.Any(i=>i.Result=="Inválida"))throw new InvalidOperationException(string.Join("\n",parsed.Issues.Select(i=>i.Details)));
            if(!await Review(job,string.Join("\n\n",parsed.Plans.Select(p=>p.Email+"\n"+string.Join("\n",p.Lines())))))return;
            job.Total=parsed.Plans.Count;bool halted=false;
            foreach(var plan in parsed.Plans) {
                if(job.Stop||halted)Add(job,new(plan.Email,"Não enviada","Operação interrompida."));
                else {Status(job,"Atualizando membros · "+plan.Email);try{var result=await Service.EditGroup(plan);Add(job,result.Row);halted=result.Stop;}catch(ApiFailure ex){Add(job,new(plan.Email,"Falhou",ex.Message));halted=ex.Reply.Stop;}}
                job.Progress++;
            }
            End(job);return;
        }
        var batch=BatchInput.Groups(input);
        if(batch.Issues.Any(i=>i.Result=="Inválida"))throw new InvalidOperationException(string.Join("\n",batch.Issues.Select(i=>i.Details)));
        var existing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var clientOf=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var stocks=new Dictionary<string,int?>();var needs=new Dictionary<string,int>();
        foreach(var group in batch.Groups) {
            if(job.Stop){End(job);return;}Status(job,"Conferindo grupo · "+group.Email);
            if(await Service.GroupExists(group.Email)==true){existing.Add(group.Email);continue;}
            var domain=group.Email.Split('@')[1];
            if(!clientOf.ContainsKey(domain)) {var stock=await Service.GroupBalance(domain);clientOf[domain]=stock?.ClientId??"domínio "+domain;if(!stocks.ContainsKey(clientOf[domain]))stocks[clientOf[domain]]=stock?.Available;}
            var client=clientOf[domain];needs[client]=needs.GetValueOrDefault(client)+1;
        }
        int purchase=needs.Sum(n=>Math.Max(0,n.Value-(stocks[n.Key]??0)));
        var summary="Grupos: "+batch.Groups.Count+"\nJá existentes: "+existing.Count+"\n"+string.Join("\n",needs.Select(n=>"Cliente "+n.Key+": necessárias "+n.Value+"; disponíveis "+(stocks[n.Key]?.ToString()??"não informadas")+"; adicionais até "+Math.Max(0,n.Value-(stocks[n.Key]??0))));
        if(stocks.Values.Any(v=>v==null))summary+="\nQuantidade livre não informada: autorização limitada a uma licença por grupo novo.";
        if(!await Review(job,summary,purchase))return;
        job.Total=batch.Groups.Count;bool stopped=false;
        foreach(var group in batch.Groups) {
            if(stopped||job.Stop){Add(job,new(group.Email,"Não enviada","Operação interrompida."));job.Progress++;continue;}
            if(existing.Contains(group.Email)){Add(job,new(group.Email,"Ignorada","Grupo já existe."));job.Progress++;continue;}
            var client=clientOf[group.Email.Split('@')[1]];var left=stocks[client];bool buy=left.GetValueOrDefault()<=0&&purchase>0;
            Status(job,"Criando grupo · "+group.Email);
            try {
                var result=await Service.CreateGroup(group,buy,()=>purchase--);
                if(result.NeedsLicense&&!buy&&!result.Stop&&!job.Stop) {
                    if(await Confirm("Saldo alterado","Autoriza contratar até 1 licença de Grupo adicional para "+group.Email+"?","Contratar 1 e continuar"))result=await Service.CreateGroup(group,true);
                    else stopped=true;
                }
                if(!buy&&result.Row.Result=="Sucesso"&&left>0)stocks[client]=left-1;
                Add(job,result.Row);stopped|=result.Stop||result.NeedsLicense;
            }catch(ApiFailure ex){Add(job,new(group.Email,"Falhou",ex.Message));stopped=ex.Reply.Stop;}
            job.Progress++;
        }
        End(job);
    }
    public async Task Reports(WebJob job,bool sent,string origins,bool originDomain,string destinations,bool destinationDomain,DateTime from,DateTime to) {
        string[] Side(string text,bool domain) {
            var values=text.Split(new[]{'\r','\n',',',';','\t',' '},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(domain&&(values.Length>1||values.Any(v=>!Planner.IsDomain(v))))throw new InvalidOperationException("Informe apenas um domínio em cada lado, ou selecione Contas de e-mail.");
            if(!domain&&values.Any(v=>!Planner.IsEmail(v)))throw new InvalidOperationException("Há um e-mail inválido no filtro.");
            return values;
        }
        var a=Side(origins,originDomain);var b=Side(destinations,destinationDomain);var mine=sent?a:b;var theirs=sent?b:a;
        if(mine.Length==0)throw new InvalidOperationException(sent?"Informe o remetente.":"Informe o destinatário.");
        to=to.Date.AddDays(1).AddSeconds(-1);if(to>DateTime.Now)to=DateTime.Now;
        AdvancedService.ValidateSearch(a.Length>0&&originDomain||b.Length>0&&destinationDomain,from,to,DateTime.Now);
        if(!await Review(job,"Período: "+from+" a "+to+"\nConsultas: "+mine.Length+"\nRemetentes: "+origins+"\nDestinatários: "+destinations))return;
        job.Total=mine.Length;
        foreach(var value in mine) {
            if(job.Stop)break;int before=job.Messages.Count;
            try {
                var scan=await Service.Messages(sent,value,theirs,from,to,r=>{job.Messages.Add(r);Notify();},t=>Status(job,t),()=>job.Stop);
                Add(job,new(value,job.Stop||scan.Truncated?"Parcial":"Sucesso",(job.Messages.Count-before)+" mensagens encontradas."+(scan.Truncated?" A API não entregou todos os registros; consulte um período menor.":"")));
            }catch(ApiFailure ex){Add(job,new(value,job.Messages.Count>before?"Parcial":"Falhou",ex.Message));if(ex.Reply.Stop)break;}
            job.Progress++;
        }
        End(job);
    }
    public async Task Replace(WebJob job,string oldMail,string newMail,string password,bool keep) {
        if(!Planner.IsEmail(oldMail)||!Planner.IsEmail(newMail)||oldMail.Equals(newMail,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Informe a conta atual e um novo endereço diferente e válido.");
        if(PasswordRules.ReplacementError(password,oldMail,newMail) is string error)throw new InvalidOperationException(error);
        if(!await Review(job,"Conta atual: "+oldMail+"\nNovo endereço: "+newMail+"\nManter apelido: "+(keep?"Sim":"Não")+"\nSenha: informada (oculta)\nAcompanhamento: até 11 minutos."))return;
        var result=await Service.Replace(oldMail,newMail,()=>password,keep,t=>Status(job,t),()=>job.Stop);password="";Add(job,result);job.Progress=1;End(job);
    }
    public Task Download(WebJob job,string mode) {
        string content=mode=="log"?string.Join("\r\n",job.Log):mode=="messages"?Exports.CsvText(new[]{"Data","Remetente","Destinatário","Assunto","Tamanho","Status"},job.Messages.Select(r=>new[]{r.Date,r.Sender,r.Recipient,r.Subject,r.Size,r.Status})):
            Exports.CsvText(new[]{"Conta","Resultado","Detalhes","Produto atual","Produto sugerido","Quota","Uso"},job.Rows.Select(r=>new[]{r.Account,r.Result,r.Details,r.Product,r.Suggested,r.Quota,r.Usage}));
        return js.InvokeVoidAsync("sky.download",Exports.FileName(job.Kind+"_"+mode),content).AsTask();
    }
    public void Dispose()=>Api.Dispose();
}
