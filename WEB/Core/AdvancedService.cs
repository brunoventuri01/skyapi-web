using System.Globalization;
using System.Text.Json;

namespace SkyAPI.Core;
public sealed class ApiFailure : InvalidOperationException {
    public ApiReply Reply {get;}
    public ApiFailure(ApiReply reply):base(reply.Message){Reply=reply;}
}
public sealed record ClientLicenseProduct(string ClientId,string ClientName,MailProduct Product,bool ShowClient) {
    public string Name=>ShowClient?Product.Name+" — "+ClientName:Product.Name;
}
public sealed record GroupStock(string ClientId,int? Available);
public sealed record ProductStock(string ClientId,int? Available,string[] Items);
/// <summary>Resultado de uma varredura de relatório: registros lidos, total anunciado pela API e se ela
/// parou antes do próprio total. Truncated é informação para a tela, não motivo para reprovar a busca.</summary>
public sealed record MessageScan(int Fetched,int? Reported,bool Truncated);
public sealed class AdvancedService {
    private readonly Func<string,string,IReadOnlyDictionary<string,string>?,Task<ApiReply>> send;
    private readonly TimeProvider time;
    private readonly Func<TimeSpan,Task> delay;
    public AdvancedService(ApiClient api):this((m,p,f)=>api.RequestAsync(m,p,f)) {}
    public AdvancedService(Func<string,string,IReadOnlyDictionary<string,string>?,Task<ApiReply>> send,TimeProvider? time=null,Func<TimeSpan,Task>? delay=null) {
        this.send=send;this.time=time??TimeProvider.System;this.delay=delay??(span=>Task.Delay(span,this.time));
    }
    private static string E(string value)=>Uri.EscapeDataString(value);
    public static void Require(ApiReply r){if(!r.Success)throw new ApiFailure(r);}
    public async Task<MailboxInfo> Mailbox(string account) {
        var r=await send("GET","mailbox/"+E(account),null);Require(r);
        return MailboxInfo.Parse(r.Data,account);
    }
    public async Task<IReadOnlyList<MailProduct>> Products(string domain) {
        if(!Planner.IsDomain(domain))throw new InvalidOperationException("Informe um domínio válido.");
        var r=await send("GET","domain/"+E(domain)+"/mail-products",null);Require(r);
        if(r.Data.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("Lista de produtos não retornada pela API.");
        return JsonValue.Array(r.Data).Select(d=>new MailProduct(JsonValue.Text(d,"name"),JsonValue.Text(d,"productId"),JsonValue.Text(d,"type")))
            .Where(p=>p.Name.Length>0).DistinctBy(p=>p.Name).ToArray();
    }
    public async Task<(HashSet<string> PurchaseAccounts,string Summary)> RestorationPurchases(IEnumerable<string> accounts) {
        var catalog=await ClientProducts();
        var batches=new Dictionary<(string Client,string Product),List<string>>();
        var productOf=new Dictionary<(string Client,string Product),MailProduct?>();
        foreach(var account in accounts) {
            var reply=await send("GET","mailbox/deleted/"+E(account),null);Require(reply);
            var client=JsonValue.Text(reply.Data,"clientId");
            if(!long.TryParse(client,out _))throw new InvalidOperationException("A API não identificou o cliente da caixa excluída "+account+".");
            var name=JsonValue.Text(reply.Data,"productname");
            if(name.Length==0)name=JsonValue.Text(reply.Data,"accounttype");
            var matches=catalog.Where(p=>p.ClientId==client && (p.Product.Name==name ||
                p.Product.Name.Replace(" Premium","")==name ||
                p.Product.Name.StartsWith("SkyExchange") && p.Product.Name.Substring(3)==name)).ToArray();
            var key=(client,matches.Length==1?matches[0].Product.Id:name);
            if(!batches.ContainsKey(key)){batches[key]=new();productOf[key]=matches.Length==1?matches[0].Product:null;}
            batches[key].Add(account);
        }
        var purchase=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var lines=new List<string>();
        foreach(var batch in batches) {
            var product=productOf[batch.Key];
            var stock=product==null?null:await ClientProductBalance(batch.Key.Client,product);
            int free=stock?.Available??0;
            foreach(var account in batch.Value.Skip(free))purchase.Add(account);
            lines.Add("Cliente "+batch.Key.Client+" · "+(product?.Name??batch.Key.Product)+
                ": necessárias "+batch.Value.Count+"; disponíveis "+(stock?.Available?.ToString()??"não informadas")+
                "; contratação de até "+Math.Max(0,batch.Value.Count-free)+".");
        }
        return(purchase,string.Join("\n",lines));
    }
    public async Task<string> ClientForDomain(string domain) {
        if(!Planner.IsDomain(domain))throw new InvalidOperationException("Domínio inválido.");
        var reply=await send("GET","domain/"+E(domain),null);Require(reply);
        var id=JsonValue.Text(reply.Data,"clientId");
        if(!long.TryParse(id,out _))throw new InvalidOperationException("A API não identificou o cliente da conta.");
        return id;
    }
    public async Task<IReadOnlyList<ClientLicenseProduct>> ClientProducts() {
        var clients=new Dictionary<string,string>();
        for(int page=1;page<=1000;page++) {
            var reply=await send("GET","client?page="+page+"&perPage=50",null);Require(reply);
            if(reply.Data.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("Lista de clientes não retornada pela API.");
            var entries=JsonValue.Array(reply.Data);int before=clients.Count;
            foreach(var entry in entries) {
                var id=JsonValue.Text(entry,"clientId");
                if(long.TryParse(id,out _))clients[id]=JsonValue.Text(entry,"name");
            }
            var pages=JsonValue.Number(reply.Root,"numPages");
            var total=JsonValue.Number(reply.Root,"total");
            if(pages.HasValue?page>=pages.Value:total.HasValue?clients.Count>=total.Value:entries.Length<50)break;
            if(clients.Count==before || page==1000)throw new InvalidOperationException("Não foi possível concluir a listagem dos clientes. Consulte novamente.");
        }
        var result=new List<ClientLicenseProduct>();
        foreach(var client in clients) {
            var reply=await send("GET","client/"+client.Key+"/product",null);Require(reply);
            if(reply.Data.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("Lista de produtos não retornada pela API.");
            foreach(var entry in JsonValue.Array(reply.Data)) {
                var product=new MailProduct(JsonValue.Text(entry,"name"),JsonValue.Text(entry,"productId"),JsonValue.Text(entry,"type"));
                if(long.TryParse(product.Id,out _) && (product.IsSkyMail || product.IsSkyExchange))
                    result.Add(new(client.Key,client.Value,product,clients.Count>1));
            }
        }
        return result.DistinctBy(p=>(p.ClientId,p.Product.Id)).ToArray();
    }
    public async Task<int?> Available(string domain,MailProduct product)=>(await ProductBalance(domain,product))?.Available;
    public async Task<ProductStock?> ProductBalance(string domain,MailProduct product) {
        // Never turn a missing balance into zero; no mailbox collection endpoint is used.
        var d=await send("GET","domain/"+E(domain),null);Require(d);
        var client=JsonValue.Text(d.Data,"clientId");
        if(!long.TryParse(client,out _) || !long.TryParse(product.Id,out _))return null;
        return await ClientProductBalance(client,product);
    }
    public async Task<ProductStock?> ClientProductBalance(string client,MailProduct product) {
        if(!long.TryParse(client,out _) || !long.TryParse(product.Id,out _))throw new InvalidOperationException("Cliente ou produto inválido.");
        var p=await send("GET","client/"+client+"/product/"+product.Id,null);
        if(p.Code==404)return null;Require(p);
        var amount=JsonValue.Number(p.Data,"amount");var count=JsonValue.Number(p.Data,"count");
        var items=JsonValue.Array(JsonValue.Get(p.Data,"items")).Select(i=>JsonValue.Text(i,"item"))
            .Where(Planner.IsEmail).ToArray();
        return new(client,amount>=0 && count>=0 && amount<=int.MaxValue ? (int)Math.Max(0,amount.Value-count.Value) : null,items);
    }
    // PUT mailbox/{conta} accepts the label the mailbox itself reports in "accounttype", which is not always the
    // catalogue name — the catalogue's "SkyMail Premium 500GB" is "SkyMail 500GB" there, and sending the
    // catalogue name answers 404. Confirmed against the API on 09/09/2026. The label is learned from a mailbox
    // already using the product; Exchange also has a known fallback for the first mailbox on a plan.
    public async Task<string> ProductLabel(string domain,MailProduct product) {
        var stock=await ProductBalance(domain,product);
        return await LabelFromStock(stock,product);
    }
    public async Task<string> ClientProductLabel(string client,MailProduct product)=>await LabelFromStock(await ClientProductBalance(client,product),product);
    public async Task<string> LabelFromStock(ProductStock? stock,MailProduct product) {
        foreach(var item in (stock?.Items??Array.Empty<string>()).Take(3)) {
            var r=await send("GET","mailbox/"+E(item),null);
            if(!r.Success)continue;
            var label=JsonValue.Text(r.Data,"accounttype");
            if(label.Length>0 && JsonValue.Text(r.Data,"productname").Equals(product.Name,StringComparison.OrdinalIgnoreCase))return label;
        }
        // The API uses Exchange (without Sky) in accounttype, including when no mailbox
        // uses the destination yet. Exchange 100GB was accepted by the live API.
        if(product.IsSkyExchange && product.Name.StartsWith("SkyExchange",StringComparison.OrdinalIgnoreCase))
            return product.Name.Substring(3);
        if(product.IsSkyMail)return product.Name.Replace("SkyMail Premium ","SkyMail ",StringComparison.OrdinalIgnoreCase);
        return "";
    }
    public async Task<(OperationRow Row, bool NeedsLicense, bool Stop)> ChangeLicense(LicenseCheck check,MailProduct product,bool purchase=false,string label="",Action? purchaseSubmitted=null) {
        var now=await Mailbox(check.Account);
        var rule=LicenseRules.Check(now,product);
        if(!rule.Eligible)return(new(rule.Account,rule.Result,rule.Details),false,false);
        // A preview becoming stale requires a new review, not a different conversion.
        if(now.Product!=check.Current)return(new(check.Account,"Ignorada","Produto mudou desde a conferência. Revise o lote."),false,false);
        var fields=new Dictionary<string,string>{["accounttype"]=label.Length>0?label:product.Name};
        if(purchase)fields["confirm_purchase"]="true";
        if(purchase)purchaseSubmitted?.Invoke();
        var r=await send("PUT","mailbox/"+E(check.Account),fields);
        if(!r.Success)return(new(check.Account,r.Stop?"Indeterminado":"Falhou",
            r.Code==404&&!r.NeedsLicense?"A API não reconheceu o produto enviado (\""+(label.Length>0?label:product.Name)+
                "\"). O nome aceito na alteração pode ser diferente do nome do catálogo; confirme com a Skymail.":r.Message),
            r.NeedsLicense,r.Stop);
        try {
            var after=await Mailbox(check.Account);
            return after.Product==product.Name
                ? (new(check.Account,"Sucesso",now.Product+" → "+product.Name),false,false)
                : (new(check.Account,"Pendente","PUT aceito, mas o produto esperado ainda não foi confirmado."),false,true);
        } catch(InvalidOperationException) {
            return(new(check.Account,"Pendente","PUT aceito; não foi possível confirmar a licença. Consulte a caixa antes de repetir."),false,true);
        }
    }
    // Group licences are stocked per client, not per domain: the client id lets the caller add up one stock only once.
    public async Task<GroupStock?> GroupBalance(string domain) {
        if(!Planner.IsDomain(domain))throw new InvalidOperationException("Domínio inválido.");
        var d=await send("GET","domain/"+E(domain),null);Require(d);
        var client=JsonValue.Text(d.Data,"clientId");
        if(!long.TryParse(client,out _))return null;
        var list=await send("GET","client/"+client+"/product",null);Require(list);
        var product=JsonValue.Array(list.Data).FirstOrDefault(p=>JsonValue.Text(p,"name")=="Grupo de E-mail");
        var id=JsonValue.Text(product,"productId");
        if(!long.TryParse(id,out _))return new(client,null);
        var detail=await send("GET","client/"+client+"/product/"+id,null);Require(detail);
        var amount=JsonValue.Number(detail.Data,"amount");var count=JsonValue.Number(detail.Data,"count");
        return new(client,amount>=0 && count>=0 && amount<=int.MaxValue ? (int)Math.Max(0,amount.Value-count.Value) : null);
    }
    // Existence is read before the batch so the licence count does not charge for groups that are already there.
    // Null means undetermined: the caller must keep counting the group as if it still had to be created.
    public async Task<bool?> GroupExists(string email) {
        var r=await send("GET","group/"+E(email),null);
        if(r.Code==404)return false;
        if(!r.Success)return null;
        return JsonValue.Text(r.Data,"mail").Equals(email,StringComparison.OrdinalIgnoreCase)?true:(bool?)null;
    }
    public async Task<(OperationRow Row,bool NeedsLicense,bool Stop)> CreateGroup(GroupInput group,bool purchase=false,Action? purchaseSubmitted=null) {
        var exists=await send("GET","group/"+E(group.Email),null);
        if(exists.Success)return(new(group.Email,"Ignorada","Grupo já existe."),false,false);
        if(exists.Code!=404){Require(exists);}
        var fields=group.Fields(); if(purchase)fields["confirm_purchase"]="true";
        if(purchase)purchaseSubmitted?.Invoke();
        var r=await send("POST","group",fields);
        if(!r.Success)return(new(group.Email,r.Stop?"Indeterminado":"Falhou",r.Message),r.NeedsLicense,r.Stop);
        if(r.Code==202)return(new(group.Email,"Pendente","Criação aceita, aguardando processamento."),false,true);
        var after=await send("GET","group/"+E(group.Email),null);
        if(!after.Success || !JsonValue.Text(after.Data,"mail").Equals(group.Email,StringComparison.OrdinalIgnoreCase))
            return(new(group.Email,"Pendente","Criação aceita; confirmação indisponível."),false,true);
        bool Has(string key,string[] values)=>values.All(v=>JsonValue.Strings(JsonValue.Get(after.Data,key)).Contains(v,StringComparer.OrdinalIgnoreCase));
        if(!Has("rfc822member",group.Members)||!Has("rfc822sender",group.Writers)||!Has("rfc822moderator",group.Moderators))
            return(new(group.Email,"Pendente","Grupo criado; membros, escritores ou moderadores não foram confirmados."),false,true);
        return(new(group.Email,"Sucesso","Grupo, membros, escritores e moderadores confirmados."),false,false);
    }
    // Confirmado na API em 09/09/2026: PUT group/{mail} com rfc822member[endereco]=true inclui e valor vazio
    // remove apenas aquele endereço, sem tocar nos demais. Mesma sintaxe dos apelidos de caixa.
    public async Task<(OperationRow Row,bool Stop)> EditGroup(GroupEditPlan plan) {
        var before=await send("GET","group/"+E(plan.Email),null);
        if(before.Code==404)return(new(plan.Email,"Ignorada","O grupo não existe."),false);
        Require(before);
        var r=await send("PUT","group/"+E(plan.Email),plan.Fields());
        if(!r.Success)return(new(plan.Email,r.Stop?"Indeterminado":"Falhou",r.Message),r.Stop);
        var after=await send("GET","group/"+E(plan.Email),null);
        if(!after.Success)return(new(plan.Email,"Pendente","Alteração aceita; confirmação indisponível."),true);
        var missed=plan.Changes.Where(c=>{
            var current=JsonValue.Strings(JsonValue.Get(after.Data,c.Role));
            return current.Contains(c.Address,StringComparer.OrdinalIgnoreCase)!=c.Add;
        }).ToArray();
        if(missed.Length>0)
            return(new(plan.Email,"Pendente","Alteração aceita; não confirmada para "+string.Join(", ",missed.Select(m=>m.Address))+"."),false);
        return(new(plan.Email,"Sucesso",plan.Summary()),false);
    }
    // Sem endpoint de coleção de caixas, a lista sai dos produtos do cliente: cada produto informa em "items"
    // as caixas que o utilizam. Assim a análise de upgrade não depende de uma planilha digitada à mão.
    public async Task<IReadOnlyList<string>> Mailboxes(string domain,Action<string>? progress=null,Func<bool>? stop=null) {
        if(!Planner.IsDomain(domain))throw new InvalidOperationException("Informe um domínio válido.");
        var d=await send("GET","domain/"+E(domain),null);Require(d);
        var client=JsonValue.Text(d.Data,"clientId");
        if(!long.TryParse(client,out _))throw new InvalidOperationException("A API não informou o cliente deste domínio.");
        // Só os produtos de caixa do domínio: sem isso a lista traria grupos, DNS e outros itens do cliente,
        // que não são caixas e responderiam 404 na consulta seguinte.
        var mailProducts=await send("GET","domain/"+E(domain)+"/mail-products",null);Require(mailProducts);
        var allowed=JsonValue.Array(mailProducts.Data).Select(p=>JsonValue.Text(p,"productId"))
            .Where(id=>id.Length>0).ToHashSet(StringComparer.Ordinal);
        var list=await send("GET","client/"+client+"/product",null);Require(list);
        var found=new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var product in JsonValue.Array(list.Data)) {
            if(stop?.Invoke()==true)break;
            var id=JsonValue.Text(product,"productId");
            if(!long.TryParse(id,out _) || !allowed.Contains(id))continue;
            var name=JsonValue.Text(product,"name");
            progress?.Invoke("Procurando caixas… "+name);
            var detail=await send("GET","client/"+client+"/product/"+id,null);
            if(!detail.Success)continue;
            foreach(var item in JsonValue.Array(JsonValue.Get(detail.Data,"items"))) {
                var address=JsonValue.Text(item,"item");
                if(BatchInput.IsMailboxAddress(address) && address.Split('@')[1].Equals(domain,StringComparison.OrdinalIgnoreCase))
                    found.Add(address);
            }
        }
        return found.ToArray();
    }
    public async Task<OperationRow> Replace(string oldMail,string newMail,Func<string> password,bool keepAlias,
        Action<string> progress,Func<bool> stop) {
        // Capture once: the editable UI may change while the rename is processing.
        var secret=password();
        if(PasswordRules.ReplacementError(secret,oldMail,newMail) is string error)return new(oldMail,"Bloqueada",error+" Nenhuma alteração foi enviada.");
        var old=await Mailbox(oldMail);
        if(old.Processing)return new(oldMail,"Pendente","Renomeação já está em processamento.");
        var target=await send("GET","mailbox/"+E(newMail),null);
        if(target.Success)return new(oldMail,"Bloqueada","O novo endereço já existe.");
        if(target.Code!=404){Require(target);return new(oldMail,"Falhou",target.Message);}
        var start=time.GetUtcNow();
        var renamed=await send("PUT","mailbox/"+E(oldMail)+"/rename",new Dictionary<string,string>{["mail"]=newMail});
        if(!renamed.Success)return new(oldMail,renamed.Stop?"Pendente":"Falhou",renamed.Message);
        MailboxInfo? current=null;
        while(time.GetUtcNow()-start<TimeSpan.FromMinutes(11)) {
            progress("Processando renomeação… "+(int)(time.GetUtcNow()-start).TotalSeconds+" / 660 segundos");
            if(stop())return new(oldMail,"Pendente","Renomeação solicitada. Acompanhamento interrompido; senha não alterada.");
            var remaining=TimeSpan.FromMinutes(11)-(time.GetUtcNow()-start);
            await delay(remaining<TimeSpan.FromSeconds(30)?remaining:TimeSpan.FromSeconds(30));
            if(time.GetUtcNow()-start>=TimeSpan.FromMinutes(11))break;
            var probe=await send("GET","mailbox/"+E(newMail),null);
            if(probe.Success) {
                try {current=MailboxInfo.Parse(probe.Data,newMail);}catch(InvalidOperationException){current=null;}
                if(current!=null && !current.Processing)break;
            } else if(probe.Stop)return new(oldMail,"Pendente","Renomeação solicitada; consulta interrompida. Senha não alterada.");
        }
        if(current==null || current.Processing)return new(oldMail,"Pendente","Renomeação ainda em processamento. A API pode levar até 10 minutos. Prazo de acompanhamento de 11 minutos encerrado; senha não alterada.");
        if(stop())return new(newMail,"Pendente","Novo endereço confirmado; senha e alias ainda não tratados.");
        progress("Novo endereço confirmado. Alterando senha…");
        ApiReply changed;
        var fields=new Dictionary<string,string>{["password"]=secret};
        try {changed=await send("PUT","mailbox/"+E(newMail),fields);}
        finally {fields.Clear();secret="";}
        if(!changed.Success)return new(newMail,"Parcial","Conta renomeada para "+newMail+". "+
            (changed.Stop?"Não foi possível confirmar a troca de senha. ":"A troca de senha foi recusada (HTTP "+changed.Code+"). ")+changed.Message+
            " O apelido antigo ainda não foi ajustado. Não repita a renomeação: conclua a senha e o apelido no novo endereço.");
        if(changed.Code==202)return new(newMail,"Pendente","Conta renomeada para "+newMail+". Troca de senha aceita e ainda em processamento. O apelido antigo ainda não foi ajustado; confira a conclusão no painel.");
        progress("Senha alterada. Verificando apelido antigo…");
        try {
            current=await Mailbox(newMail);
            var otherAliases=current.Aliases.Where(a=>!a.Equals(oldMail,StringComparison.OrdinalIgnoreCase)).ToArray();
            bool hasOld=current.Aliases.Contains(oldMail,StringComparer.OrdinalIgnoreCase);
            if(hasOld!=keepAlias) {
                // Update exactly one keyed value. Never DELETE the alias collection.
                var alias=await send("PUT","mailbox/"+E(newMail),new Dictionary<string,string>{["mailalternateaddress["+oldMail+"]"]=keepAlias?"true":""});
                if(!alias.Success)return new(newMail,"Pendente","Renomeação e senha concluídas; ajuste do apelido não confirmado.");
            }
            var final=await Mailbox(newMail);
            if(final.Aliases.Contains(oldMail,StringComparer.OrdinalIgnoreCase)!=keepAlias ||
                otherAliases.Any(a=>!final.Aliases.Contains(a,StringComparer.OrdinalIgnoreCase)))
                return new(newMail,"Pendente","Renomeação e senha concluídas; confira os apelidos.");
            return new(newMail,"Sucesso","Renomeação e senha concluídas. Endereço antigo "+(keepAlias?"mantido como apelido.":"removido dos apelidos."));
        } catch(InvalidOperationException) {return new(newMail,"Pendente","Renomeação e senha concluídas; verificação dos apelidos indisponível.");}
    }
    // Domínio em QUALQUER um dos lados limita a busca a 7 dias, e não só no lado que vai para a API: a
    // varredura é a mesma, o volume também, e a API não sustenta janelas longas nesse caso.
    public const int DomainDays=7,AccountDays=90;
    public static readonly string DomainWindow="Busca com domínio em qualquer um dos lados é limitada a "+
        DomainDays+" dias. Escolha um período menor, ou use contas de e-mail nos dois lados.";
    public static int MaxDays(bool anyDomain)=>anyDomain?DomainDays:AccountDays;
    public static bool HasDomainSide(string primary,IEnumerable<string> counterparts)=>
        (primary.Length>0 && !primary.Contains('@')) || counterparts.Any(c=>c.Length>0 && !c.Contains('@'));
    public static void ValidateDates(DateTime from,DateTime to,DateTime now)=>ValidateSearch(false,from,to,now);
    public static void ValidateSearch(bool anyDomain,DateTime from,DateTime to,DateTime now) {
        if(from>to || to>now || from<now.AddDays(-AccountDays))throw new InvalidOperationException("Escolha um período dentro dos últimos 90 dias, sem datas futuras.");
        if(anyDomain && (to-from).TotalDays>DomainDays+0.01)throw new InvalidOperationException(DomainWindow);
    }
    // O filtro aceita um endereço completo ou um domínio; vazio quer dizer "qualquer um".
    public static bool Matches(string address,string filter) {
        if(filter.Length==0)return true;
        return filter.Contains('@')
            ? address.Equals(filter,StringComparison.OrdinalIgnoreCase)
            : address.EndsWith("@"+filter,StringComparison.OrdinalIgnoreCase);
    }
    // Origem e destino aceitam endereço completo ou domínio: é assim que se busca "tudo que o domínio X mandou
    // para o domínio Y". A API filtra os dois lados; a conferência local repete o filtro sobre o que voltou.
    // Uma consulta só, pelo lado que é seu ("tudo que o meu domínio recebeu no período"), e todo o resto
    // peneirado aqui: o outro lado aceita vários filtros, cada um endereço completo ou domínio.
    // A API para de entregar páginas muito antes do total que ela mesma anuncia — medido em 09/09/2026:
    // 350 registros de 1003 anunciados, sempre em página cheia seguida de página vazia. Em vez de devolver o
    // relatório pela metade, o período é dividido ao meio e cada metade é consultada de novo, como o relatório
    // de login já fazia. Os registros de uma fatia só entram no resultado quando a fatia fecha inteira: senão
    // a parte já lida entraria duas vezes ao reconsultar.
    private const int PageSize=50,SliceFloorSeconds=60,OffsetCeiling=200_000;
    private static DateTime WholeSecond(DateTime value)=>new(value.Ticks-value.Ticks%TimeSpan.TicksPerSecond,value.Kind);
    public async Task<MessageScan> Messages(bool sent,string primary,IReadOnlyList<string> counterparts,DateTime from,DateTime to,
        Action<MessageRow> record,Action<string> progress,Func<bool> stop) {
        if(primary.Length==0)throw new InvalidOperationException(sent?"Informe o remetente.":"Informe o destinatário.");
        // Domínio em qualquer um dos lados encurta a janela, inclusive no lado que é peneirado aqui.
        ValidateSearch(HasDomainSide(primary,counterparts),from,to,DateTime.Now);
        var pending=new Stack<(DateTime From,DateTime To)>();
        pending.Push((WholeSecond(from),WholeSecond(to)));
        int fetched=0; decimal? announced=null; bool truncated=false;
        while(pending.Count>0 && !stop()) {
            var slice=pending.Pop();
            var part=await Scan(sent,primary,counterparts,slice.From,slice.To,progress,stop);
            // O total do período inteiro é o da primeira consulta; cada fatia anuncia só o próprio pedaço.
            announced??=part.Reported;
            // Só o corte por volume se resolve dividindo o período. Página repetida quer dizer que a API
            // está ignorando o offset, e fatiar mais só multiplicaria chamadas repetindo o mesmo registro.
            if(part.Capped && part.Divisible && (slice.To-slice.From).TotalSeconds>=SliceFloorSeconds) {
                var half=slice.From.AddSeconds((long)((slice.To-slice.From).TotalSeconds/2));
                pending.Push((half.AddSeconds(1),slice.To));pending.Push((slice.From,half));
                continue;
            }
            if(part.Capped)truncated=true;
            foreach(var row in part.Rows)record(row);
            fetched+=part.Fetched;
        }
        return new(fetched,announced is >=0 and <=int.MaxValue?(int)announced.Value:null,truncated);
    }
    /// <summary>Uma fatia de período, paginada até o fim. Capped diz que a API parou antes do próprio total:
    /// página cheia seguida de página vazia, página repetida ou teto de segurança do offset.</summary>
    private sealed record SlicePart(List<MessageRow> Rows,int Fetched,decimal? Reported,bool Capped,bool Divisible);
    private async Task<SlicePart> Scan(bool sent,string primary,IReadOnlyList<string> counterparts,DateTime from,DateTime to,
        Action<string> progress,Func<bool> stop) {
        var kept=new List<MessageRow>();
        int offset=0,fetched=0; decimal? reported=null; bool capped=false,divisible=false,lastFull=false,retried=false;
        var pages=new HashSet<string>();
        while(!stop()) {
            progress("Consultando mensagens… "+primary+" · "+from.ToString("dd/MM HH:mm")+" a "+to.ToString("dd/MM HH:mm")+
                " · "+offset+" registros consultados");
            var q=new Dictionary<string,string>{["from"]=from.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture),
                ["to"]=to.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture),
                ["limit"]=PageSize.ToString(CultureInfo.InvariantCulture),["offset"]=offset.ToString(CultureInfo.InvariantCulture)};
            // Medido em 09/09/2026: domínio só é aceito no parâmetro principal (fromEmail no enviadas, toEmail
            // no recebidas). No parâmetro secundário a API devolve zero, sem erro, quando recebe um domínio —
            // por isso o outro lado nunca é enviado, e sim conferido aqui.
            q[sent?"fromEmail":"toEmail"]=primary;
            var r=await send("GET","report/messages/"+(sent?"sent":"received")+"?"+string.Join("&",q.Select(x=>E(x.Key)+"="+E(x.Value))),null);Require(r);
            if(r.Data.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("Resposta de relatório inválida.");
            var rows=JsonValue.Array(r.Data);
            var total=JsonValue.Number(r.Root,"total_count");
            if(total>=0)reported=reported.HasValue?Math.Max(reported.Value,total!.Value):total;
            if(rows.Length==0) {
                // Página vazia com total à frente pode ser tropeço momentâneo: tenta uma vez do mesmo ponto.
                if(reported>offset && !retried){retried=true;await delay(TimeSpan.FromSeconds(1));continue;}
                // Página cheia seguida de vazia é a assinatura do corte da API. Última página curta quer dizer
                // que os dados acabaram mesmo, e que o total anunciado é que estava adiantado.
                capped=divisible=reported>offset && lastFull;
                break;
            }
            retried=false;
            // Página repetida seria laço infinito: encerra a fatia e deixa a divisão do período resolver.
            if(!pages.Add(r.Data.GetRawText())){capped=true;break;}  // não divisível: dividir repetiria tudo
            foreach(var row in rows) {
                var parsed=MessageRecord.Parse(row);
                var mine=sent?parsed.Sender:parsed.Recipient;
                var theirs=sent?parsed.Recipient:parsed.Sender;
                if(!Matches(mine,primary))continue;
                if(counterparts.Count>0 && !counterparts.Any(filter=>Matches(theirs,filter)))continue;
                kept.Add(parsed);
            }
            lastFull=rows.Length>=PageSize;
            fetched+=rows.Length;offset+=rows.Length;
            if(offset>=OffsetCeiling){capped=true;break;}
            if(reported.HasValue){if(offset>=reported.Value)break;}
            else if(!lastFull)break;
        }
        return new(kept,fetched,reported,capped,divisible);
    }
    public async Task<bool> Logins(string account,DateTime from,DateTime to,Action<LoginRow> record,
        Action<string>? progress=null,Func<bool>? stop=null) {
        ValidateDates(from,to,DateTime.Now);
        // Login has no documented offset: partition inclusive intervals to avoid silent truncation.
        var pending=new Stack<(DateTime From,DateTime To)>();
        pending.Push((new DateTime(from.Ticks-from.Ticks%TimeSpan.TicksPerSecond,from.Kind),
            new DateTime(to.Ticks-to.Ticks%TimeSpan.TicksPerSecond,to.Kind)));
        bool complete=true;
        while(pending.Count>0) {
            if(stop?.Invoke()==true)return false;
            var slice=pending.Pop();
            progress?.Invoke("Consultando login… "+account+" · "+slice.From+" até "+slice.To);
            var path="report/login?user="+E(account)+"&from="+E(slice.From.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture))+
                "&to="+E(slice.To.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture))+"&limit=50";
            var r=await send("GET",path,null);Require(r);
            // The live API answers data:{total_results,messages:[…]}; a bare array in data is still accepted.
            var payload=r.Data.ValueKind==JsonValueKind.Array?r.Data:JsonValue.Get(r.Data,"messages");
            if(payload.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("Resposta de login inválida.");
            var rows=JsonValue.Array(payload);
            var reported=JsonValue.Number(r.Data,"total_results")??JsonValue.Number(r.Root,"total_count");
            bool capped=rows.Length>=50 || reported>rows.Length;
            if(capped && slice.To>slice.From) {
                var seconds=(long)(slice.To-slice.From).TotalSeconds;
                var midpoint=slice.From.AddSeconds(seconds/2);
                pending.Push((midpoint.AddSeconds(1),slice.To));pending.Push((slice.From,midpoint));continue;
            }
            if(capped)complete=false;
            foreach(var d in rows) {
                var who=LoginRecord.Account(d);
                if(who.Equals(account,StringComparison.OrdinalIgnoreCase))record(LoginRecord.Parse(d,who));
            }
        }
        return complete;
    }
}
