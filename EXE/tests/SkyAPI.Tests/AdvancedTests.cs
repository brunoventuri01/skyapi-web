using System.Net;
using System.Text;
using System.Text.Json;
using SkyAPI.Core;

internal static class AdvancedTests {
    private static ApiReply Reply(object data,int code=200,bool success=true,bool needs=false,bool stop=false) =>
        new(code,success,JsonSerializer.SerializeToElement(new{success,data}),needs,success?"OK":"Falha controlada",stop);
    private static object Box(string mail,string product="SkyMail 5GB",bool processing=false,string[]? aliases=null)=>new {
        mail,accounttype=product,renameprocessing=processing,mailalternateaddress=aliases??Array.Empty<string>(),
        mailBoxQuotaSize=new{mailquotasize=5L*1024*1024*1024,used=4L*1024*1024*1024}
    };
    private sealed class Clock:TimeProvider {
        public DateTimeOffset Now=new(2026,9,9,12,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>Now;
        public Task Delay(TimeSpan span){Now+=span;return Task.CompletedTask;}
    }
    public static async Task Run(Action<bool,string> check) {
        var catalogCalls=new List<string>();
        var clientService=new AdvancedService((method,path,fields)=>{
            catalogCalls.Add(path);
            if(path.StartsWith("client?")) {
                int id=path.Contains("page=1&")?10:20;
                return Task.FromResult(new ApiReply(200,true,JsonSerializer.SerializeToElement(new{
                    success=true,data=new[]{new{clientId=id,name="Cliente "+id}},numPages=2,total=2}),false,"OK"));
            }
            return Task.FromResult(Reply(new[]{new{productId=44,name="SkyMail 5GB"},new{productId=11,name="DNS"},new{productId=60,name="SkyExchange Basic 25GB"}}));
        });
        var clientProducts=await clientService.ClientProducts();
        check(clientProducts.Count==4 && clientProducts.All(p=>p.ShowClient),"1.1.11: produtos identificam cliente quando conexao acessa mais de um");
        check(catalogCalls.Count==4 && catalogCalls.All(p=>!p.StartsWith("domain")),"1.1.11: catalogo paginado sem dominio");
        check(clientProducts.All(p=>p.Product.Name!="DNS"),"1.1.11: catalogo exclui produtos que nao sao caixas");
        check(AdvancedPaths.IsAllowed("GET","client?page=1&perPage=50") && !AdvancedPaths.IsAllowed("POST","client"),"1.1.11: somente leitura de clientes liberada");
        var restoreAccounts=Enumerable.Range(1,15).Select(i=>"teste"+i+"@empresa.com.br").ToArray();
        var restoreService=new AdvancedService((method,path,fields)=>Task.FromResult(
            path.StartsWith("client?")?Reply(new[]{new{clientId=10,name="Cliente"}}):
            path=="client/10/product"?Reply(new[]{new{productId=44,name="SkyMail 5GB"}}):
            path.StartsWith("mailbox/deleted/")?Reply(new{clientId=10,accounttype="SkyMail 5GB"}):
            Reply(new{amount=2,count=0,items=Array.Empty<object>()})));
        var restorePlan=await restoreService.RestorationPurchases(restoreAccounts);
        check(restorePlan.PurchaseAccounts.Count==13 && restoreAccounts.Take(2).All(a=>!restorePlan.PurchaseAccounts.Contains(a)),
            "contratacao previa: 15 restauracoes, saldo 2, autoriza somente 13");
        check(restorePlan.Summary.Contains("necessárias 15") && restorePlan.Summary.Contains("disponíveis 2"),
            "contratacao previa: resumo mostra necessidade e saldo antes de enviar");
        int writes=0,purchases=0;var updated=new HashSet<string>();
        var purchaseService=new AdvancedService((method,path,fields)=>{
            var account=Uri.UnescapeDataString(path.Substring("mailbox/".Length));
            if(method=="GET")return Task.FromResult(Reply(Box(account,updated.Contains(account)?"SkyMail 25GB":"SkyMail 5GB")));
            writes++;if(fields?.ContainsKey("confirm_purchase")==true)purchases++;
            updated.Add(account);return Task.FromResult(Reply(new{}));
        });
        int submitted=0;
        foreach(var account in restoreAccounts) {
            var result=await purchaseService.ChangeLicense(new(account,"SkyMail 5GB","SkyMail 25GB","Pronta",""),
                new("SkyMail 25GB","6","email"),restorePlan.PurchaseAccounts.Contains(account),purchaseSubmitted:()=>submitted++);
            check(result.Row.Result=="Sucesso","contratacao previa: conta concluida na primeira tentativa");
        }
        check(writes==15 && purchases==13 && submitted==13,"contratacao previa: 15 envios, 13 autorizacoes, sem falhas provocadas ou reenvios");
        await purchaseService.ChangeLicense(new(restoreAccounts[0],"SkyMail 25GB","SkyMail 25GB","Pronta",""),
            new("SkyMail 25GB","6","email"),true,purchaseSubmitted:()=>submitted++);
        check(submitted==13,"contratacao previa: conta ja no destino nao consome autorizacao");
        using(var limiterApi=new ApiClient(new RateHandler())) {
            limiterApi.SetToken(Jwt.Create("test",Encoding.UTF8.GetBytes("not-a-real-key")));
            var watch=System.Diagnostics.Stopwatch.StartNew();
            await limiterApi.RequestAsync("GET","domain/empresa.com.br");
            await limiterApi.ExecuteAsync(new("ana@empresa.com.br","PUT","mailbox/ana@empresa.com.br",new Dictionary<string,string>(),"Teste"));
            check(watch.ElapsedMilliseconds>=500,"rate limit: consultas e operacoes compartilham intervalo");
        }
        using(var limiterApi=new ApiClient(new RateHandler(true))) {
            limiterApi.SetToken(Jwt.Create("test",Encoding.UTF8.GetBytes("not-a-real-key")));
            bool waiting=false;limiterApi.RateLimitWaiting+=until=>{if(until.HasValue)waiting=true;};
            await limiterApi.RequestAsync("GET","domain/empresa.com.br");
            using var cancel=new CancellationTokenSource(100);
            var canceled=await limiterApi.ExecuteAsync(new("ana@empresa.com.br","PUT","mailbox/ana@empresa.com.br",new Dictionary<string,string>(),"Teste"),cancel.Token);
            check(waiting && canceled.StopBatch,"rate limit: saldo de requisicoes zerado espera e permite cancelar sem enviar");
        }
        const string mail="ana@empresa.com.br",next="bia@empresa.com.br";
        var exchangeProducts=SkyExchangeLine.Products;
        for(int i=0;i<exchangeProducts.Count;i++) {
            var product=exchangeProducts[i];
            var current=new MailboxInfo(mail,product.Name,product.CapacityBytes,product.CapacityBytes*.9m,Array.Empty<string>(),"",false);
            check(LicenseRules.Check(current,product).Result=="Ignorada","1.1.9: Exchange sem alteracao "+product.Name);
            var plan=LicenseRules.Plan(current,Array.Empty<MailProduct>());
            check(i==exchangeProducts.Count-1?plan==null:plan?.Product==exchangeProducts[i+1]&&plan.InPanel==false,
                "1.1.9: proximo tamanho Exchange fora do painel "+product.Name);
            if(i<exchangeProducts.Count-1) {
                var destination=exchangeProducts[i+1] with {Id="100",Type=i==0?"exchange2013basic":"exchange2013"};
                check(LicenseRules.Check(current,destination).Eligible,"1.1.9: Exchange pode alterar dentro da familia "+product.Name);
                check(LicenseRules.Plan(current,new[]{destination})?.InPanel==true,"1.1.9: Exchange contratado reconhecido "+product.Name);
            }
            check(LicenseRules.Suggest(current,SkyMailLine.Products)==null,"1.1.9: Exchange nao sugere SkyMail "+product.Name);
        }
        check(PasswordRules.ReplacementError("12345678901234567890")!=null,"1.1.9: senha numerica recusada antes da operacao");
        check(PasswordRules.ReplacementError("Abc1234")!=null,"1.1.9: senha com menos de 8 caracteres recusada");
        check(PasswordRules.ReplacementError("abc12345")!=null,"1.1.9: dois tipos de senha recusados");
        foreach(var valid in new[]{"Rmvn2958","rmvn295!","RMVN295!","Rmvnkpt!"})
            check(PasswordRules.ReplacementError(valid)==null,"1.1.9: aceita cada combinacao de tres tipos");
        int weakCalls=0;
        var noRename=new AdvancedService((m,p,f)=>{weakCalls++;return Task.FromResult(Reply(new{}));});
        var weakResult=await noRename.Replace(mail,next,()=>"1234567890",false,_=>{},()=>false);
        check(weakResult.Result=="Bloqueada"&&weakCalls==0,"1.1.9: senha fraca nao envia nenhuma chamada");
        foreach(var invalid in new[]{"Rt!12358","Rt!98752","Rt!aBc58","Rt!ZyX58","Rt!qWe58","Rt!EwQ58","Rt!89052"})
            check(PasswordRules.ReplacementError(invalid)!=null,"1.1.10: sequencias diretas, inversas e teclado bloqueadas");
        foreach(var invalid in new[]{"Bruno!58","BRÚNO!58","Venturi!58","Skydemo!58","Marina!58"})
            check(PasswordRules.ReplacementError(invalid,"bruno.venturi@skydemo.com.br","marina@empresa.com.br")!=null,
                "1.1.10: nomes e dominio bloqueados sem diferenciar caixa ou acentos");
        var personalResult=await noRename.Replace("bruno@empresa.com.br",next,()=>"Bruno!58",false,_=>{},()=>false);
        check(personalResult.Result=="Bloqueada"&&weakCalls==0,"1.1.10: dados pessoais bloqueados antes de chamadas");
        var generatedPasswords=Enumerable.Range(0,100).Select(_=>PasswordRules.Generate("bruno@skydemo.com.br","marina@empresa.com.br")).ToArray();
        check(generatedPasswords.All(p=>p.Length==10&&PasswordRules.ReplacementError(p,"bruno@skydemo.com.br","marina@empresa.com.br")==null)
            &&generatedPasswords.Distinct().Count()==100,"1.1.10: gerador produz 100 senhas distintas que atendem todas as regras");
        foreach(int status in new[]{400,202}) {
            var steps=new Queue<ApiReply>(new[]{Reply(Box(mail)),Reply(new{},404,false),Reply(new{}),Reply(Box(next)),
                Reply(new{},status,status==202)});
            int reads=0;string sentPassword="";
            var replacementClock=new Clock();
            var partialService=new AdvancedService((m,p,f)=>{
                if(f?.ContainsKey("password")==true)sentPassword=f["password"];
                return Task.FromResult(steps.Dequeue());
            },replacementClock,replacementClock.Delay);
            var result=await partialService.Replace(mail,next,()=>++reads==1?"Rmvn2958":"CHANGED",false,_=>{},()=>false);
            check(result.Result==(status==400?"Parcial":"Pendente")&&result.Details.Contains("apelido antigo ainda não foi ajustado")&&steps.Count==0,
                "1.1.9: senha recusada ou pendente nao prossegue com apelido "+status);
            check(reads==1&&sentPassword=="Rmvn2958","1.1.9: senha validada preservada durante a renomeacao "+status);
        }
        var unusedExchange=new AdvancedService((m,p,f)=>Task.FromResult(p.StartsWith("domain/")?
            Reply(new{clientId=1}):Reply(new{amount=1,count=0,items=Array.Empty<object>()})));
        check(await unusedExchange.ProductLabel("empresa.com.br",new("SkyExchange 100GB","100","exchange2013"))=="Exchange 100GB",
            "1.1.9: Exchange sem caixa existente usa rotulo de API");
        foreach(var failure in new[]{"Very weak password. PRIVATE", "Senha inválida. Você deve completar uma senha diferente da atual. PRIVATE", "UNKNOWN PRIVATE"}) {
            using var passwordTransport=new ApiClient(new PasswordHandler(failure));
            passwordTransport.SetToken(Jwt.Create("test",Encoding.UTF8.GetBytes("not-a-real-key")));
            var rejectedPassword=await passwordTransport.RequestAsync("PUT","mailbox/"+Uri.EscapeDataString(mail),new Dictionary<string,string>{["password"]="Test_Only_Secret119!"});
            check(!rejectedPassword.Success&&!rejectedPassword.Message.Contains("PRIVATE")&&
                (failure.StartsWith("Very")?rejectedPassword.Message.Contains("3 tipos"):
                 failure.StartsWith("Senha")?rejectedPassword.Message.Contains("igual à senha atual"):rejectedPassword.Message.Contains("Dados rejeitados")),
                "1.1.9: rejeicao de senha traduzida sem expor resposta bruta");
        }
        void ExpectThrow(Action action,string name){try{action();check(false,name);}catch(InvalidOperationException){check(true,name);}}
        // Names confirmed live: the catalogue name lives in productname; accounttype is a shorter label.
        var catalogued=MailboxInfo.Parse(JsonSerializer.SerializeToElement(new{
            mail="ana@empresa.com.br",accounttype="Exchange 50GB",productname="SkyExchange 50GB",
            renameprocessing=false,mailalternateaddress=Array.Empty<string>()}),"ana@empresa.com.br");
        check(catalogued.Product=="SkyExchange 50GB","v1.1: produto da caixa vem de productname, não de accounttype");
        // Confirmed live: the PUT takes the mailbox label, not the catalogue name, and the label is learned
        // from a mailbox already on the product.
        var labelCalls=new List<string>();
        var labelService=new AdvancedService((m,path,f)=>{
            labelCalls.Add(m+" "+path);
            if(path.StartsWith("domain/"))return Task.FromResult(Reply(new{clientId=40958}));
            if(path=="client/40958/product/302")return Task.FromResult(Reply(new{amount=2,count=1,
                items=new[]{new{item="brunoventuri.skydemo.com.br"},new{item="premium@empresa.com.br"}}}));
            return Task.FromResult(Reply(new{mail="premium@empresa.com.br",accounttype="SkyMail 500GB",
                productname="SkyMail Premium 500GB",renameprocessing=false,mailalternateaddress=Array.Empty<string>()}));
        });
        var premiumProduct=new MailProduct("SkyMail Premium 500GB","302","email");
        check(await labelService.ProductLabel("empresa.com.br",premiumProduct)=="SkyMail 500GB",
            "v1.1: rótulo aceito no accounttype é aprendido de uma caixa que já usa o produto");
        check(!labelCalls.Any(c=>c.Contains("brunoventuri.skydemo.com.br")&&c.StartsWith("GET mailbox")),
            "v1.1: itens que não são endereços de caixa são ignorados ao aprender o rótulo");
        labelCalls.Clear();
        var noItems=new AdvancedService((m,path,f)=>path.StartsWith("domain/")
            ?Task.FromResult(Reply(new{clientId=40958}))
            :Task.FromResult(Reply(new{amount=1,count=0,items=Array.Empty<object>()})));
        check(await noItems.ProductLabel("empresa.com.br",premiumProduct)=="SkyMail 500GB",
            "v1.1: produto sem caixas não inventa rótulo");
        var sentType="";
        var labelled=new AdvancedService((m,path,f)=>{
            if(m=="PUT"){sentType=f!["accounttype"];return Task.FromResult(Reply(new{}));}
            return Task.FromResult(Reply(new{mail="ana@empresa.com.br",accounttype="SkyMail 500GB",
                productname=sentType.Length>0?"SkyMail Premium 500GB":"SkyMail 5GB",
                renameprocessing=false,mailalternateaddress=Array.Empty<string>()}));
        });
        var labelledResult=await labelled.ChangeLicense(new("ana@empresa.com.br","SkyMail 5GB","SkyMail Premium 500GB","Pronta",""),
            premiumProduct,false,"SkyMail 500GB");
        check(sentType=="SkyMail 500GB"&&labelledResult.Row.Result=="Sucesso",
            "v1.1: alteração envia o rótulo aprendido e valida contra o nome do catálogo");
        var rejected=new AdvancedService((m,path,f)=>m=="PUT"
            ?Task.FromResult(new ApiReply(404,false,default,false,"Registro não encontrado ou indisponível para esta operação."))
            :Task.FromResult(Reply(new{mail="ana@empresa.com.br",accounttype="SkyMail 5GB",productname="SkyMail 5GB",
                renameprocessing=false,mailalternateaddress=Array.Empty<string>()})));
        var rejectedResult=await rejected.ChangeLicense(new("ana@empresa.com.br","SkyMail 5GB","SkyMail Premium 500GB","Pronta",""),
            premiumProduct,false,"");
        check(rejectedResult.Row.Details.Contains("não reconheceu o produto")&&rejectedResult.Row.Details.Contains("SkyMail Premium 500GB"),
            "v1.1: 404 no produto explica divergência de nome em vez de registro não encontrado");
        check(LicenseRules.Check(catalogued,new("SkyMail 25GB","6","email")).Details==LicenseRules.ExchangeBlocked,
            "v1.1: caixa Exchange orienta abrir chamado, com o nome real da API");
        var premium=MailboxInfo.Parse(JsonSerializer.SerializeToElement(new{
            mail="ana@empresa.com.br",accounttype="SkyMail 500GB",productname="SkyMail Premium 500GB",
            renameprocessing=false,mailalternateaddress=Array.Empty<string>()}),"ana@empresa.com.br");
        check(LicenseRules.Check(premium,new("SkyMail Premium 500GB","302","email")).Result=="Ignorada",
            "v1.1: caixa já no produto do catálogo não é alterada");
        check(new MailProduct("SkyExchange Basic 25GB","60","exchange2013basic").IsSkyMail==false
            &&new MailProduct("SkyMail Premium 500GB","302","email").IsSkyMail,"v1.1: tipos exchange2013 reconhecidos");
        var input=BatchInput.Accounts("\uFEFFemail\r\n"+mail+"\r\n"+mail.ToUpperInvariant()+"\r\ninválida");
        check(input.Accounts.Count==1&&input.Issues.Count==2,"v1.1: BOM, cabeçalho, duplicata e inválida");
        foreach(char separator in new[]{',',';','\t'})check(BatchInput.Accounts(mail+separator+next).Accounts.Count==2,"v1.1: separador "+(int)separator);
        ExpectThrow(()=>BatchInput.Accounts(mail,"outro.com.br"),"v1.1: domínio validado");
        ExpectThrow(()=>BatchInput.Accounts("\"aspas"),"v1.1: CSV inválido bloqueado");
        ExpectThrow(()=>BatchInput.Accounts(new string('x',5_000_001)),"v1.1: limite de entrada");
        // Uma pessoa por linha, repetindo o grupo: evita depender do separador | dentro da celula.
        var mergedGroups=BatchInput.Groups("grupo,email,membros,escritores,moderadores\r\n"+
            "Suporte,suporte@empresa.com.br,"+mail+",,\r\n"+
            "Suporte,suporte@empresa.com.br,"+next+",,\r\n").Groups;
        check(mergedGroups.Count==1&&mergedGroups[0].Members.Length==2
            &&mergedGroups[0].Members.Contains(mail)&&mergedGroups[0].Members.Contains(next),
            "v1.1: linhas repetidas do mesmo grupo somam membros");
        // Gestao de membros: uma linha por alteracao, agrupadas em uma unica chamada por grupo.
        var edits=BatchInput.GroupEdits(BatchInput.GroupEditHeader+"\r\n"+
            "fin@empresa.com.br,remover,membro,"+mail+"\r\n"+
            "fin@empresa.com.br,adicionar,moderador,"+next+"\r\n"+
            "fin@empresa.com.br,remover,membro,"+mail+"\r\n"+
            "sup@empresa.com.br,adicionar,escritor,"+next+"\r\n"+
            "sup@empresa.com.br,girar,membro,"+next+"\r\n");
        check(edits.Plans.Count==2&&edits.Plans[0].Changes.Count==2,"v1.1: alteracoes agrupadas por grupo");
        check(edits.Issues.Any(i=>i.Details.Contains("repetida"))&&edits.Issues.Any(i=>i.Details.Contains("adicionar ou remover")),
            "v1.1: alteracao repetida e acao desconhecida viram avisos");
        var editFields=edits.Plans[0].Fields();
        check(editFields["rfc822member["+mail+"]"]==""&&editFields["rfc822moderator["+next+"]"]=="true"
            &&editFields.Count==2&&edits.Plans[1].Fields()["rfc822sender["+next+"]"]=="true",
            "v1.1: remocao envia valor vazio e inclusao envia true");
        ExpectThrow(()=>BatchInput.GroupEdits("grupo,acao,papel\r\na@empresa.com.br,remover,membro"),"v1.1: cabecalho de alteracao conferido");
        var editCalls=new List<(string Method,string Path,Dictionary<string,string> Fields)>();
        var editReplies=new Queue<ApiReply>();
        var editService=new AdvancedService((m,path,f)=>{
            editCalls.Add((m,path,f==null?new():new(f)));
            return Task.FromResult(editReplies.Count>0?editReplies.Dequeue():Reply(new{}));
        });
        editReplies.Enqueue(Reply(new{mail="fin@empresa.com.br"}));
        editReplies.Enqueue(Reply(new{}));
        editReplies.Enqueue(Reply(new{mail="fin@empresa.com.br",rfc822member=Array.Empty<string>(),rfc822moderator=new[]{next}}));
        var edited=await editService.EditGroup(edits.Plans[0]);
        check(edited.Row.Result=="Sucesso"&&editCalls.Count(c=>c.Method=="PUT")==1,"v1.1: edicao de grupo usa um unico PUT confirmado");
        editCalls.Clear();editReplies.Enqueue(Reply(new{},404,false));
        check((await editService.EditGroup(edits.Plans[0])).Row.Result=="Ignorada"&&!editCalls.Any(c=>c.Method=="PUT"),
            "v1.1: grupo inexistente nao recebe alteracao");
        editCalls.Clear();
        editReplies.Enqueue(Reply(new{mail="fin@empresa.com.br"}));
        editReplies.Enqueue(Reply(new{}));
        editReplies.Enqueue(Reply(new{mail="fin@empresa.com.br",rfc822member=new[]{mail},rfc822moderator=new[]{next}}));
        check((await editService.EditGroup(edits.Plans[0])).Row.Result=="Pendente","v1.1: remocao nao confirmada fica pendente");
        check(AdvancedPaths.IsAllowed("PUT","group/fin%40empresa.com.br")&&!AdvancedPaths.IsAllowed("DELETE","group/fin"),
            "v1.1: PUT de grupo liberado, DELETE nao");
        // Descoberta de caixas do dominio pelos produtos do cliente, sem planilha.
        var discovery=new AdvancedService((m,path,f)=>{
            if(path.EndsWith("/mail-products"))return Task.FromResult(Reply(new[]{new{name="SkyMail 5GB",productId=44,type="email"}}));
            if(path.StartsWith("domain/"))return Task.FromResult(Reply(new{clientId=40958}));
            if(path=="client/40958/product")return Task.FromResult(Reply(new[]{new{name="SkyMail 5GB",productId=44},new{name="Grupo de E-mail",productId=4}}));
            if(path=="client/40958/product/44")return Task.FromResult(Reply(new{items=new[]{
                new{item=mail},new{item="fora@outrodominio.com.br"},new{item=mail.ToUpperInvariant()}}}));
            return Task.FromResult(Reply(new{items=new[]{new{item="grupo@empresa.com.br"}}}));
        });
        var discovered=await discovery.Mailboxes("empresa.com.br");
        check(discovered.Count==1&&discovered[0]==mail,
            "v1.1: caixas do dominio saem dos produtos de e-mail, sem grupos, repeticoes nem outro dominio");
        var group=BatchInput.Groups("grupo\temail\tmembros\tescritores\tmoderadores\nFinanceiro\tfin@empresa.com.br\t"+mail+"|"+next+"\t"+mail+"\t"+next).Groups.Single();
        var gf=group.Fields();
        check(gf["rfc822member["+mail+"]"]=="true"&&gf["rfc822sender["+mail+"]"]=="true"&&gf["rfc822moderator["+next+"]"]=="true","v1.1: arrays de grupo usam chaves por endereço");
        check(!gf.ContainsKey("confirm_purchase")&&!gf.Keys.Any(k=>k.Contains("alternate")),"v1.1: grupo sem compra automática ou aliases");
        check(!BatchInput.IsMailboxAddress("\"x[y]\"@empresa.com.br"),"v1.1: endereço não pode injetar chaves de arrays");
        var quoted=BatchInput.Groups("grupo,email,membros,escritores,moderadores\n\"Financeiro, BR\",fin@empresa.com.br,\""+mail+","+next+"\",,");
        check(quoted.Groups.Single().Members.Length==2,"v1.1: CSV de grupo com aspas e listas");
        var p25=new MailProduct("SkyMail 25GB","6","email");
        var box=MailboxInfo.Parse(Reply(Box(mail)).Data,mail);
        check(box.Percent==80m,"v1.1: quota em bytes calcula 80%");
        check(LicenseRules.Suggest(box,new[]{new MailProduct("SkyMail 1GB","1","email"),p25})==p25,"v1.1: upgrade nunca recomenda downgrade");
        check(LicenseRules.Suggest(box,new[]{new MailProduct("SkyExchange 50GB","9","exchange")})==null,"v1.1: upgrade bloqueia conversão Exchange");
        check(new MailProduct("SkyMail Premium 500 GB","3","email").CapacityBytes==500m*1073741824m,"v1.1: capacidade com espaço no nome");
        check(LicenseRules.Check(box,new MailProduct("SkyExchange 50GB","9","exchange")).Result=="Bloqueada","v1.1: SkyMail → Exchange bloqueado");
        var exBox=box with {Product="SkyExchange 50GB"};
        check(LicenseRules.Check(exBox,p25).Details==LicenseRules.ExchangeBlocked,"v1.1: Exchange → SkyMail bloqueado");
        var calls=new List<(string Method,string Path,Dictionary<string,string> Fields)>();
        var replies=new Queue<ApiReply>();
        Task<ApiReply> Send(string m,string path,IReadOnlyDictionary<string,string>? f){
            calls.Add((m,path,f==null?new():new(f)));return Task.FromResult(replies.Dequeue());
        }
        var service=new AdvancedService(Send);
        replies.Enqueue(Reply(Box(mail,"SkyMail 25GB")));
        var skipped=await service.ChangeLicense(new(mail,"SkyMail 25GB",p25.Name,"Ignorada",""),p25);
        check(skipped.Row.Result=="Ignorada"&&calls.Count==1&&calls[0].Method=="GET","v1.1: licença igual não envia PUT");
        calls.Clear();replies.Enqueue(Reply(Box(mail)));replies.Enqueue(Reply(new{}));replies.Enqueue(Reply(Box(mail,"SkyMail 25GB")));
        var changed=await service.ChangeLicense(new(mail,"SkyMail 5GB",p25.Name,"Pronta",""),p25);
        var put=calls.Single(c=>c.Method=="PUT");
        check(changed.Row.Result=="Sucesso"&&calls.Last().Method=="GET","v1.1: pós-validação obrigatória");
        check(put.Fields.Count==1&&put.Fields["accounttype"]=="SkyMail 25GB","v1.1: PUT parcial usa nome do produto, sem productId");
        calls.Clear();replies.Enqueue(Reply(Box(mail)));replies.Enqueue(Reply(new{}));replies.Enqueue(Reply(Box(mail)));
        var mismatch=await service.ChangeLicense(new(mail,"SkyMail 5GB",p25.Name,"Pronta",""),p25);
        check(mismatch.Row.Result=="Pendente"&&mismatch.Stop,"v1.1: PUT sem produto confirmado não vira sucesso");
        calls.Clear();replies.Enqueue(Reply(Box(mail)));replies.Enqueue(Reply(new{},400,false,true));
        var shortage=await service.ChangeLicense(new(mail,"SkyMail 5GB",p25.Name,"Pronta",""),p25);
        check(shortage.NeedsLicense&&calls.Count==2&&!calls.Last().Fields.ContainsKey("confirm_purchase"),"v1.1: falta de licença não compra nem repete automaticamente");
        calls.Clear();replies.Enqueue(Reply(Box(mail)));replies.Enqueue(Reply(new{}));replies.Enqueue(Reply(Box(mail,"SkyMail 25GB")));
        await service.ChangeLicense(new(mail,"SkyMail 5GB",p25.Name,"Pronta",""),p25,true);
        check(calls.Single(c=>c.Method=="PUT").Fields["confirm_purchase"]=="true","v1.1: compra apenas com autorização explícita");
        calls.Clear();replies.Enqueue(Reply(Box(mail,"SkyMail 10GB")));
        var stale=await service.ChangeLicense(new(mail,"SkyMail 5GB",p25.Name,"Pronta",""),p25);
        check(stale.Row.Result=="Ignorada"&&calls.Count==1,"v1.1: conferência obsoleta exige revisão");
        calls.Clear();replies.Enqueue(Reply(new[]{new{name="SkyMail 25GB",productId=6,type="email"}}));
        var products=await service.Products("empresa.com.br");
        check(products.Count==1&&products[0].Name==p25.Name,"v1.1: catálogo só contém produtos retornados");
        calls.Clear();replies.Enqueue(Reply(new{clientId=8080}));replies.Enqueue(Reply(new{amount=20,count=19}));
        check(await service.Available("empresa.com.br",p25)==1,"v1.1: disponibilidade usa quantidade contratada e ocupada");
        calls.Clear();replies.Enqueue(Reply(new{clientId=8080}));replies.Enqueue(Reply(new{amount=20}));
        check(await service.Available("empresa.com.br",p25)==null,"v1.1: disponibilidade ausente não vira zero");
        calls.Clear();replies.Enqueue(Reply(new{},404,false));replies.Enqueue(Reply(new{},201));
        replies.Enqueue(Reply(new{mail=group.Email,rfc822member=group.Members,rfc822sender=group.Writers,rfc822moderator=group.Moderators}));
        var created=await service.CreateGroup(group);
        check(created.Row.Result=="Sucesso"&&calls[1].Path=="group"&&calls[1].Method=="POST","v1.1: grupo criado e pós-validado");
        calls.Clear();replies.Enqueue(Reply(new{mail=group.Email}));
        check((await service.CreateGroup(group)).Row.Result=="Ignorada"&&calls.Count==1,"v1.1: grupo existente é ignorado");
        bool purchaseConsumed=false;
        calls.Clear();replies.Enqueue(Reply(new{mail=group.Email}));
        await service.CreateGroup(group,true,()=>purchaseConsumed=true);
        check(!purchaseConsumed&&calls.Count==1,"v1.1: grupo existente não consome confirmação de compra");
        calls.Clear();replies.Enqueue(Reply(new{},404,false));replies.Enqueue(Reply(new{},400,false,true));
        await service.CreateGroup(group,true,()=>purchaseConsumed=true);
        check(purchaseConsumed&&calls.Single(c=>c.Method=="POST").Fields["confirm_purchase"]=="true","v1.1: confirmação consumida somente no envio do POST");
        calls.Clear();replies.Enqueue(Reply(new{clientId=8080}));
        replies.Enqueue(Reply(new[]{new{name="Grupo de E-mail",productId=4}}));
        replies.Enqueue(Reply(new{amount=10,count=7}));
        var stock=await service.GroupBalance("empresa.com.br");
        check(stock!=null&&stock.Available==3,"v1.1: saldo de grupos consultado antes do lote");
        check(stock!=null&&stock.ClientId=="8080","v1.1: saldo de grupos identifica o cliente dono do estoque");
        calls.Clear();replies.Enqueue(Reply(new{clientId=8080}));
        replies.Enqueue(Reply(new[]{new{name="Outro produto",productId=9}}));
        var noProduct=await service.GroupBalance("empresa.com.br");
        check(noProduct!=null&&noProduct.Available==null,"v1.1: produto de grupo ausente mantém saldo desconhecido");
        check(noProduct!=null&&noProduct.ClientId=="8080","v1.1: cliente é identificado mesmo sem produto de grupo");
        calls.Clear();replies.Enqueue(Reply(new{},404,false));
        check(await service.GroupExists("novo@empresa.com.br")==false,"v1.1: grupo inexistente confirmado por 404");
        calls.Clear();replies.Enqueue(Reply(new{mail="fin@empresa.com.br"}));
        check(await service.GroupExists("fin@empresa.com.br")==true,"v1.1: grupo existente dispensa licença na conferência");
        calls.Clear();replies.Enqueue(Reply(new{},500,false,false,true));
        check(await service.GroupExists("fin@empresa.com.br")==null,"v1.1: existência indeterminada continua contando licença");
        calls.Clear();replies.Enqueue(Reply(new{mail="outro@empresa.com.br"}));
        check(await service.GroupExists("fin@empresa.com.br")==null,"v1.1: resposta de outro grupo não confirma existência");
        var clock=new Clock();var rename=new AdvancedService(Send,clock,clock.Delay);
        calls.Clear();replies.Enqueue(Reply(Box(mail)));replies.Enqueue(Reply(new{},404,false));
        replies.Enqueue(Reply(new{renameprocessing=true}));replies.Enqueue(Reply(new{},404,false));
        replies.Enqueue(Reply(Box(next,aliases:new[]{mail,"outro@empresa.com.br"})));
        replies.Enqueue(Reply(new{}));
        replies.Enqueue(Reply(Box(next,aliases:new[]{mail,"outro@empresa.com.br"})));
        replies.Enqueue(Reply(new{}));
        replies.Enqueue(Reply(Box(next,aliases:new[]{"outro@empresa.com.br"})));
        var replacement=await rename.Replace(mail,next,()=>"Test_Only_Secret119!",false,_=>{},()=>false);
        check(replacement.Result=="Sucesso"&&clock.Now.Minute==1,"v1.1: rename aguarda e verifica a cada 30 segundos");
        var passwordIndex=calls.FindIndex(c=>c.Fields.ContainsKey("password"));
        check(passwordIndex>calls.FindIndex(c=>c.Path.EndsWith("/rename"))&&calls[passwordIndex-1].Method=="GET","v1.1: senha somente após novo endereço confirmado");
        var aliasPut=calls.Single(c=>c.Fields.Keys.Any(k=>k.StartsWith("mailalternateaddress")));
        check(aliasPut.Fields.Count==1&&aliasPut.Fields["mailalternateaddress["+mail+"]"]==""&&!calls.Any(c=>c.Method=="DELETE"),"v1.1: exclui somente alias antigo, sem DELETE coletivo");
        calls.Clear();var timeoutClock=new Clock();int probes=0;
        var timeoutService=new AdvancedService((m,path,f)=>{
            calls.Add((m,path,f==null?new():new(f)));
            if(path.EndsWith("/rename"))return Task.FromResult(Reply(new{renameprocessing=true}));
            if(path=="mailbox/"+Uri.EscapeDataString(mail))return Task.FromResult(Reply(Box(mail)));
            probes++;return Task.FromResult(Reply(new{},404,false));
        },timeoutClock,timeoutClock.Delay);
        var timeout=await timeoutService.Replace(mail,next,()=>"Test_Only_Secret119!",true,_=>{},()=>false);
        check(timeout.Result=="Pendente"&&timeoutClock.Now.Minute==11&&!calls.Any(c=>c.Fields.ContainsKey("password")),"v1.1: timeout de 11 minutos fica pendente e não altera senha");
        check(probes==22,"v1.1: polling limitado a 11 minutos");
        ExpectThrow(()=>AdvancedService.ValidateDates(DateTime.Now.AddDays(-91),DateTime.Now,DateTime.Now),"v1.1: bloqueia consulta além de 90 dias");
        check(AdvancedService.Matches("a@gmail.com","gmail.com")&&!AdvancedService.Matches("a@fakegmail.com","gmail.com")
            &&AdvancedService.Matches("a@gmail.com","a@gmail.com")&&!AdvancedService.Matches("b@gmail.com","a@gmail.com")
            &&AdvancedService.Matches("qualquer@x.com",""),"v1.1: filtro aceita domínio exato, endereço ou vazio");
        check(!AdvancedPaths.IsAllowed("GET","mailbox")&&!AdvancedPaths.IsAllowed("DELETE","mailbox/x/mailalternateaddress")&&!AdvancedPaths.IsAllowed("POST","mailbox"),"v1.1: rotas proibidas não disponíveis");
        var messages=new List<MessageRow>();int page=0;var now=DateTime.Now;
        var paged=new AdvancedService((m,path,f)=>{
            page++;var data=page==1?new[]{new{De="a@gmail.com",Para=mail,Assunto="A",Tamanho="1",Motivo="Recebida",timestamp=now}}:
                new[]{new{De="b@fakegmail.com",Para=mail,Assunto="B",Tamanho="2",Motivo="Recebida",timestamp=now}};
            return Task.FromResult(new ApiReply(200,true,JsonSerializer.SerializeToElement(new{success=true,total_count=2,data}),false,""));
        });
        await paged.Messages(false,mail,new[]{"gmail.com"},now.AddDays(-1),now,messages.Add,_=>{},()=>false);
        check(page==2&&messages.Count==1,"v1.1: paginação e filtro local do outro lado aplicados juntos");
        var pairCalls=new List<string>();
        var crossed=new AdvancedService((m,path,f)=>{
            pairCalls.Add(path);
            return Task.FromResult(new ApiReply(200,true,JsonSerializer.SerializeToElement(
                new{success=true,total_count=0,data=Array.Empty<object>()}),false,""));
        });
        await crossed.Messages(true,"empresa.com.br",new[]{"cliente.com.br","ana@outro.com.br"},now.AddDays(-1),now,_=>{},_=>{},()=>false);
        check(pairCalls.Single().Contains("fromEmail=empresa.com.br")&&!pairCalls.Single().Contains("toEmail")
            &&pairCalls.Single().StartsWith("report/messages/sent"),
            "v1.1: so o lado proprio vai para a API; o outro lado nunca e enviado");
        pairCalls.Clear();
        await crossed.Messages(false,"empresa.com.br",Array.Empty<string>(),now.AddDays(-1),now,_=>{},_=>{},()=>false);
        check(pairCalls.Single().Contains("toEmail=empresa.com.br")&&!pairCalls.Single().Contains("fromEmail"),
            "v1.1: nas recebidas o lado proprio e o destinatario");
        // Vários filtros do outro lado: basta um bater.
        var manyPage=0;var manyRows=new List<MessageRow>();
        var many=new AdvancedService((m,path,f)=>{
            manyPage++;
            var data=manyPage==1?new[]{
                new{De="a@gmail.com",Para=mail,Assunto="A",timestamp=now},
                new{De="b@hotmail.com",Para=mail,Assunto="B",timestamp=now},
                new{De="c@outlook.com",Para=mail,Assunto="C",timestamp=now}}:Array.Empty<object>();
            return Task.FromResult(new ApiReply(200,true,JsonSerializer.SerializeToElement(
                new{success=true,total_count=3,data}),false,""));
        });
        await many.Messages(false,mail,new[]{"gmail.com","c@outlook.com"},now.AddDays(-1),now,manyRows.Add,_=>{},()=>false);
        check(manyRows.Count==2&&manyRows.Any(r=>r.Sender=="a@gmail.com")&&manyRows.Any(r=>r.Sender=="c@outlook.com"),
            "v1.1: outro lado aceita varios filtros, dominio ou endereco");
        var missingSide=false;
        try {await crossed.Messages(true,"",new[]{"cliente.com.br"},now.AddDays(-1),now,_=>{},_=>{},()=>false);}
        catch(InvalidOperationException){missingSide=true;}
        check(missingSide,"v1.1: envio exige o remetente");
        // 1.1.7 — total anunciado adiantado com a última página curta: os dados acabaram mesmo, e isso deixou
        // de derrubar a busca inteira. Uma nova tentativa antes de encerrar, e o que veio é entregue.
        var driftRows=new List<MessageRow>();int driftPage=0;
        var drift=new AdvancedService((m,path,f)=>{
            driftPage++;
            var data=driftPage==1
                ?Enumerable.Range(0,30).Select(i=>new{De="a@gmail.com",Para=mail,Assunto="A"+i,timestamp=now}).ToArray()
                :Array.Empty<object>();
            return Task.FromResult(new ApiReply(200,true,JsonSerializer.SerializeToElement(
                new{success=true,total_count=90,data}),false,""));
        },null,_=>Task.CompletedTask);
        var driftScan=await drift.Messages(false,mail,Array.Empty<string>(),now.AddDays(-1),now,driftRows.Add,_=>{},()=>false);
        check(driftRows.Count==30&&driftScan.Fetched==30&&driftScan.Reported==90&&!driftScan.Truncated&&driftPage==3,
            "v1.1.7: total adiantado com página curta entrega o que veio, sem derrubar nem dividir a busca");
        // 1.1.7 — API que corta a paginação antes do próprio total: o período é dividido e o relatório sai
        // inteiro. Medido ao vivo em 09/09/2026: 350 registros entregues de 1003 anunciados.
        static (DateTime From,DateTime To,int Offset) Query(string path) {
            var fields=path.Split('?')[1].Split('&').Select(x=>x.Split('=')).ToDictionary(x=>x[0],x=>Uri.UnescapeDataString(x[1]));
            var style=System.Globalization.CultureInfo.InvariantCulture;
            return (DateTime.Parse(fields["from"],style),DateTime.Parse(fields["to"],style),int.Parse(fields["offset"],style));
        }
        var universe=Enumerable.Range(0,120).Select(i=>(Stamp:DateTime.Today.AddDays(-5).AddHours(i),Subject:"M"+i)).ToArray();
        int cutCalls=0;
        var cut=new AdvancedService((m,path,f)=>{
            cutCalls++;
            var q=Query(path);
            var window=universe.Where(x=>x.Stamp>=q.From&&x.Stamp<=q.To).OrderBy(x=>x.Stamp).ToArray();
            // O corte: acima de 100 registros a API para de entregar, mas continua anunciando o total.
            var data=q.Offset>=100?Array.Empty<object>()
                :window.Skip(q.Offset).Take(50).Select(x=>(object)new{De="a@gmail.com",Para=mail,Assunto=x.Subject,timestamp=x.Stamp}).ToArray();
            return Task.FromResult(new ApiReply(200,true,JsonSerializer.SerializeToElement(
                new{success=true,total_count=window.Length,data}),false,""));
        },null,_=>Task.CompletedTask);
        var cutRows=new List<MessageRow>();
        var cutScan=await cut.Messages(false,mail,Array.Empty<string>(),DateTime.Today.AddDays(-5).AddHours(-1),DateTime.Now,
            cutRows.Add,_=>{},()=>false);
        check(cutRows.Count==120&&cutRows.Select(r=>r.Subject).Distinct().Count()==120&&cutScan.Fetched==120
            &&!cutScan.Truncated&&cutScan.Reported==120&&cutCalls>4,
            "v1.1.7: corte da API divide o período e o relatório sai inteiro, sem repetir registro");
        int loopPage=0;var loopRows=new List<MessageRow>();
        var looping=new AdvancedService((m,path,f)=>{
            loopPage++;
            var data=new[]{new{De="a@gmail.com",Para=mail,Assunto="igual",timestamp=now}};
            return Task.FromResult(new ApiReply(200,true,JsonSerializer.SerializeToElement(
                new{success=true,total_count=500,data}),false,""));
        },null,_=>Task.CompletedTask);
        var loopScan=await looping.Messages(false,mail,Array.Empty<string>(),now.AddDays(-1),now,loopRows.Add,_=>{},()=>false);
        check(loopPage==2&&loopRows.Count==1&&loopScan.Truncated,
            "v1.1.7: página repetida encerra a paginação sem laço e sem exceção");
        // Janela de 7 dias: vale para domínio dos dois lados, não só do lado que vai para a API.
        check(AdvancedService.HasDomainSide("empresa.com.br",Array.Empty<string>())
            &&AdvancedService.HasDomainSide(mail,new[]{"gmail.com"})
            &&!AdvancedService.HasDomainSide(mail,new[]{"a@gmail.com"})
            &&AdvancedService.MaxDays(true)==7&&AdvancedService.MaxDays(false)==90,
            "v1.1.7: domínio de qualquer lado encurta a janela; contas dos dois lados mantêm 90 dias");
        ExpectThrow(()=>AdvancedService.ValidateSearch(true,DateTime.Now.AddDays(-8),DateTime.Now,DateTime.Now),
            "v1.1.7: oito dias com domínio bloqueado");
        AdvancedService.ValidateSearch(false,DateTime.Now.AddDays(-30),DateTime.Now,DateTime.Now);
        check(true,"v1.1.7: trinta dias só com contas continua permitido");
        var wideSecondary=false;
        try {await crossed.Messages(false,mail,new[]{"gmail.com"},now.AddDays(-30),now,_=>{},_=>{},()=>false);}
        catch(InvalidOperationException){wideSecondary=true;}
        check(wideSecondary,"v1.1.7: domínio no lado peneirado também limita a consulta a 7 dias");
        await crossed.Messages(false,mail,new[]{"a@gmail.com"},now.AddDays(-30),now,_=>{},_=>{},()=>false);
        check(true,"v1.1.7: contas dos dois lados consultam 30 dias sem bloqueio");
        check(Display.Usage(1600000000m,3m)=="1,49 GB · 3%"&&Display.Usage(null,null)==""
            &&!Display.Usage(1600000000m,2.7m).Contains("%%"),
            "v1.1.7: uso sai com um único sinal de porcentagem");
        // Exibicao: tamanho legivel, data em pt-BR e conserto do texto corrompido pela API.
        check(Display.Size("10428")=="10,2 KB"&&Display.Size("1500000")=="1,43 MB"&&Display.Size("512")=="512 bytes"
            &&Display.Size("")==""&&Display.Size("abc")=="","v1.1: tamanho em KB ou MB, conforme a grandeza");
        check(Display.Gigabytes(53687091200m)=="50 GB"&&Display.Gigabytes(5368709120m)=="5 GB"
            &&Display.Gigabytes(null)==""&&Display.Gigabytes(-1m)=="","v1.1: quota e uso em GB");
        check(Display.When("2026-10-31T15:45:00-03:00").StartsWith("31/10/2026 às ")
            &&Display.When("")==""&&Display.When("nao e data")=="nao e data","v1.1: data legivel, sem inventar quando nao reconhece");
        var quebrado=System.Text.Encoding.Latin1.GetString(System.Text.Encoding.UTF8.GetBytes("Rosângela ação"));
        check(quebrado!="Rosângela ação"&&Display.Repair(quebrado)=="Rosângela ação","v1.1: assunto corrompido pela API é remontado");
        check(Display.Repair("Rosângela")=="Rosângela"&&Display.Repair("plain ascii")=="plain ascii"
            &&Display.Repair("Ã sozinho")=="Ã sozinho","v1.1: texto sadio nao e alterado pelo conserto");
        // Shapes copied from the live API: received keeps Tamanho only in the embedded JSON, sent has no @timestamp.
        var received=MessageRecord.Parse(JsonSerializer.SerializeToElement(new Dictionary<string,object>{
            ["De"]="noreply@openai.com",["Para"]=mail,["Assunto"]="assunto",
            ["@timestamp"]="2026-09-04T13:45:26-03:00",["MailBox"]="INBOX",
            ["message"]="{\"Assunto\":\"com espaco \",\"Tamanho\":62832}"}));
        check(received.Date.StartsWith("04/09/2026 às ")&&received.Size=="61,4 KB"&&received.Subject=="assunto"
            &&received.Status=="INBOX","v1.1: recebida usa tamanho do JSON embutido e a pasta como status");
        var sentRow=MessageRecord.Parse(JsonSerializer.SerializeToElement(new{
            De=mail,Para="externo@fora.com.br",Assunto="teste",DataEnvio="2026-09-02T14:02:25-03:00",
            timestamp="2026-09-02T14:02:27-03:00",Motivo="(250 2.0.0 Ok)",message="Mensagem enviada com sucesso"}));
        check(sentRow.Date.StartsWith("02/09/2026 às ")&&sentRow.Status=="(250 2.0.0 Ok)"&&sentRow.Size=="",
            "v1.1: mensagem enviada usa DataEnvio quando não há @timestamp");
        int loginQueries=0;var logins=new List<LoginRow>();
        var partitioned=new AdvancedService((m,path,f)=>{
            loginQueries++;int count=loginQueries==1?50:1;
            var page=Enumerable.Range(0,count).Select(i=>new Dictionary<string,object>{
                ["User"]=mail,["Motivo"]="ok",["@timestamp"]="2026-09-08T11:54:05.435475-03:00",
                ["_id"]="2335c801-ab95-11f1-be86-020014780281",
                ["message"]="{\"RemoteIP\":\"168.90.191.162\",\"Protocolo\":\"painel\"}"
            }).ToArray();
            return Task.FromResult(Reply(new{total_results=count,messages=page}));
        });
        bool loginComplete=await partitioned.Logins(mail,now.AddSeconds(-5),now,logins.Add);
        check(loginComplete&&loginQueries==3&&logins.Count==2,"v1.1: login divide intervalo para evitar truncamento");
        check(logins[0].Account==mail&&logins[0].Date.StartsWith("2026-09-08")&&logins[0].Status=="ok"
            &&logins[0].Ip=="168.90.191.162"&&logins[0].Protocol=="painel","v1.1: login lê User, @timestamp, Motivo e RemoteIP do envelope real");
        var legacyLogins=new List<LoginRow>();
        var legacy=new AdvancedService((m,path,f)=>Task.FromResult(Reply(new[]{new{User=mail,Motivo="ok",timestamp=now}})));
        await legacy.Logins(mail,now.AddSeconds(-1),now,legacyLogins.Add);
        check(legacyLogins.Count==1&&legacyLogins[0].Ip=="","v1.1: array direto em data continua aceito no login");
        var directory=Path.Combine(Path.GetTempPath(),"skyapi-tests-"+Guid.NewGuid());
        using(var log=new OperationLog(directory,"teste"))log.Write("teste",mail,"Troca","Sucesso",TimeSpan.FromSeconds(2));
        var logText=File.ReadAllText(Directory.GetFiles(directory).Single());
        using(var parsedLog=JsonDocument.Parse(logText))check(parsedLog.RootElement[0].EnumerateObject().Count()==7&&!logText.Contains("SECRET"),"v1.1: JSON tem apenas campos permitidos");
        foreach(var file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);
        check(Exports.FileName("Licenças").StartsWith("SkyAPI_Licenças_")&&Exports.FileName("Licenças").EndsWith(".csv"),"v1.1: nome padrão de exportação");
        check(Exports.CsvText(new[]{"Assunto"},new[]{new[]{"=CMD()"}}).Contains("'=CMD()"),"v1.1: CSV protege contra fórmulas");
        using var transport=new ApiClient(new LicenseHandler());
        transport.SetToken(Jwt.Create("test",Encoding.UTF8.GetBytes("not-a-real-key")));
        var refusal=await transport.RequestAsync("PUT","mailbox/"+Uri.EscapeDataString(mail),new Dictionary<string,string>{["accounttype"]="SkyMail 25GB"});
        check(refusal.NeedsLicense&&!refusal.Success&&!refusal.Message.Contains("PRIVATE"),"v1.1: recusa de licença classificada sem vazar resposta");

    }
    private sealed class RateHandler(bool exhausted=false):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {
            var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"success\":true,\"data\":{}}")};
            response.Headers.Add("X-RateLimit-Limit","120");
            response.Headers.Add("X-RateLimit-Remaining",exhausted?"0":"119");
            response.Headers.Add("X-RateLimit-Reset",DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds().ToString());
            return Task.FromResult(response);
        }
    }
    private sealed class LicenseHandler:HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent("{\"success\":false,\"message\":\"Não existem licenças disponíveis PRIVATE\"}")});
    }
    private sealed class PasswordHandler(string message):HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest){Content=new StringContent(JsonSerializer.Serialize(new{success=false,message}))});
    }
}
