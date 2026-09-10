using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyAPI.Core;
class Tests {
 static int passed,failed;
 static void Check(bool ok,string name){if(ok){passed++;Console.WriteLine("PASS "+name);}else{failed++;Console.WriteLine("FAIL "+name);}}
 static void Throws(Action f,string name){try{f();Check(false,name);}catch(InvalidOperationException){Check(true,name);}}
 static Plan P(Operation o,string s,string status="disabled",string password="")=>Planner.Build(o,s,status,password);
 static async Task Main(){
  const string mail="ana@exemplo.com.br";const string list="ana@exemplo.com.br\nbruno@exemplo.com.br";
  Check(P(Operation.DeleteAccounts,list).Items.Count==2,"Leitura de lista com duas contas");
  Check(P(Operation.DeleteAccounts,"\uFEFF"+mail+"\r\n\r\n").Items.Count==1,"BOM, CRLF e linhas vazias");
  Check(P(Operation.DeleteAccounts,mail).Items.Single().Method=="DELETE","Exclusão usa DELETE");
  Check(P(Operation.DeleteAccounts,"bad@\nhttps://fake.com").Errors.Count==2,"E-mails inválidos bloqueados");
  Check(P(Operation.DeleteAccounts,mail+"\n"+mail.ToUpperInvariant()).Errors.Count>0,"Duplicatas sem distinção de caixa bloqueadas");
  Check(P(Operation.DeleteAccounts,"").Errors.Count>0,"Lote vazio bloqueado");
  Check(P(Operation.DeleteDns,"exemplo.com.br").Items.Single().Path=="dns/exemplo.com.br","Endpoint de zona DNS");
  Check(P(Operation.DeleteDns,"https://exemplo.com.br").Errors.Count>0,"Domínio com protocolo bloqueado");
  Check(P(Operation.DeleteDns,"-ruim.com.br").Errors.Count>0,"Domínio com hífen inicial bloqueado");
  Check(P(Operation.DeleteGroups,mail).Items.Single().Path.StartsWith("group/"),"Endpoint de grupos");
  Check(P(Operation.RestoreAccounts,mail).Items.Single().Path=="mailbox/deleted/ana%40exemplo.com.br/restore","Endpoint de restauração e URL encoding");
  foreach(var s in new[]{"disabled","noaccess","active"})Check(P(Operation.AccountStatus,mail,s).Items.Single().Fields["accountstatus"]==s,"Status "+s);
  Check(P(Operation.AccountStatus,mail,"unknown").Errors.Count>0,"Status fora da lista bloqueado");
  Check(P(Operation.PasswordSame,mail).Errors.Count>0,"Senha vazia bloqueada");
  Check(P(Operation.PasswordSame,mail,password:"S&+;123").Items.Single().Fields["password"]=="S&+;123","Senha preservada");
  Check(P(Operation.PasswordDifferent,mail+";  S;en,ha+123  ").Items.Single().Fields["password"]=="  S;en,ha+123  ","Senha individual preserva espaços e separadores");
  Check(P(Operation.PasswordDifferent,mail+";").Errors.Count>0,"Senha individual vazia bloqueada");
  Check(P(Operation.PasswordDifferent,mail).Errors.Count>0,"Senha individual sem separador bloqueada");
  Check(P(Operation.ForcePasswordChange,mail).Items.Single().Fields["forcepasswordchange"]=="true","Forçar senha sem alterar a atual");
  Check(P(Operation.RenameAccounts,mail+",nova@exemplo.com.br").Items.Single().Fields["mail"]=="nova@exemplo.com.br","Renomeação com vírgula");
  Check(P(Operation.RenameAccounts,mail+";nova@exemplo.com.br").Errors.Count==0,"Renomeação com ponto e vírgula");
  Check(P(Operation.RenameAccounts,mail+","+mail).Errors.Count>0,"Renomeação para si bloqueada");
  Check(P(Operation.RenameAccounts,mail+",x@exemplo.com.br\nb@exemplo.com.br,x@exemplo.com.br").Errors.Count>0,"Destinos duplicados bloqueados");
  Check(P(Operation.RenameAccounts,mail+",b@exemplo.com.br\nb@exemplo.com.br,c@exemplo.com.br").Errors.Count>0,"Cadeia conflitante bloqueada");
  var attrs=P(Operation.Attributes,"mailbox,Nome,Cargo\n"+mail+",\"Silva, Ana\",Gerente");
  Check(attrs.Errors.Count==0&&attrs.Items.Single().Fields["displayname"]=="Silva, Ana","CSV com vírgula entre aspas");
  Check(P(Operation.Attributes,"mailbox;Nome;Cargo\n"+mail+";Ana;Gerente").Errors.Count==0,"Atributos separados por ponto e vírgula");
  Check(P(Operation.Attributes,"mailbox,Nome,Cargo\n"+mail+",Ana,").Items.Single().Fields.Count==1,"Campos vazios não apagados");
  Check(P(Operation.Attributes,"Nome\nAna").Errors.Count>0,"Cabeçalho mailbox obrigatório");
  Check(P(Operation.Attributes,"mailbox,Nome,displayname\n"+mail+",Ana,Ana").Errors.Count>0,"Aliases duplicados bloqueados");
  Check(P(Operation.Attributes,"mailbox,desconhecido\n"+mail+",X").Errors.Count>0,"Atributos desconhecidos bloqueados");
  Check(P(Operation.Attributes,"mailbox,Nome\n"+mail+",Ana,Extra").Errors.Count>0,"Contagem de colunas validada");
  Check(P(Operation.Attributes,"mailbox,Celular\n"+mail+",11999990001").Items.Single().Fields["mobilephone"]=="11 9999-90001","Formato de 11 dígitos preserva contrato do script");
  Check(P(Operation.Attributes,"mailbox,Telefone comercial\n"+mail+",1133330001").Items.Single().Fields["companyphone"]=="11 3333-0001","Telefone de 10 dígitos");
  Check(P(Operation.Attributes,"mailbox,Celular\n"+mail+",123").Errors.Count>0,"Telefone curto bloqueado");
  Check(P(Operation.Attributes,"mailbox,Celular\n"+mail+",5511999990001").Errors.Count>0,"Telefone longo não truncado silenciosamente");
  Check(P(Operation.Attributes,"mailbox,Nome\n"+mail+",\"aberta").Errors.Count>0,"Aspas desbalanceadas bloqueadas");
  Check(P(Operation.DeleteAccounts,new string('x',5_000_001)).Errors.Count>0,"Limite de tamanho");
  Check(Csv.Parse("a,b\n\"a\"\"b\",\"c\nd\"")[1][0]=="a\"b","Aspas escapadas e quebra dentro de campo");
  Check(Csv.Cell("=CMD()") == "\"'=CMD()\"","Fórmulas desativadas no relatório");
  var key=Encoding.UTF8.GetBytes("sample-key-never-production-123456");string jwt=Jwt.Create("jti-test",key);Jwt.Validate(jwt);Check(true,"JWT HS256 com jti válido");
  var pieces=jwt.Split('.');var sign=Convert.ToBase64String(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(pieces[0]+"."+pieces[1]))).TrimEnd('=').Replace('+','-').Replace('/','_');Check(sign==pieces[2],"Assinatura HMAC conferida independentemente");
  Throws(()=>Jwt.Validate("a.b.c"),"JWT malformado rejeitado");Throws(()=>Jwt.Validate(""),"JWT vazio rejeitado");
  string B(string s)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+','-').Replace('/','_');
  Throws(()=>Jwt.Validate(B("{\"alg\":\"none\"}")+"."+pieces[1]+"."+pieces[2]),"JWT sem assinatura segura rejeitado");
  Throws(()=>Jwt.Validate(pieces[0]+"."+B("{\"jti\":\"x\",\"exp\":1}")+"."+pieces[2]),"JWT expirado rejeitado");
  var fake=new Fake();using var api=new ApiClient(fake);api.SetToken(jwt);var item=P(Operation.PasswordSame,mail,password:"A&+; 123").Items.Single();
  var ok=await api.ExecuteAsync(item);Check(ok.Status=="Sucesso","HTTP 200 confirmado");Check(fake.Uri==ApiClient.BaseUrl+item.Path&&fake.Method=="PUT","URL e método corretos");Check(fake.Auth=="Bearer "+jwt,"Bearer enviado");Check(fake.Body=="password=A%26%2B%3B+123","Form URL encoding protege senha com símbolos");
  foreach(var code in new[]{201,204}){fake.Code=code;Check((await api.ExecuteAsync(item)).Status=="Sucesso","HTTP "+code+" confirmado");}
  foreach(var code in new[]{401,403,429,500,302}){fake.Code=code;Check((await api.ExecuteAsync(item)).StopBatch,"HTTP "+code+" interrompe lote");}
  fake.Code=202;Check((await api.ExecuteAsync(item)).Status=="Pendente","HTTP 202 não é conclusão");fake.Code=404;Check(!(await api.ExecuteAsync(item)).StopBatch,"404 registrado individualmente");
  fake.ThrowTimeout=true;var uncertain=await api.ExecuteAsync(item);Check(uncertain.Status=="Indeterminado"&&uncertain.StopBatch,"Timeout não provoca reenvio");fake.ThrowTimeout=false;
  fake.ThrowNetwork=true;Check((await api.ExecuteAsync(item)).Status=="Indeterminado","Falha de rede não presume resultado");fake.ThrowNetwork=false;
  fake.Code=200;fake.Response="{\"data\":{\"jti\":\"abc123\"}}";await api.LoginAsync(mail,"pwd",Convert.ToBase64String(key));Check(api.HasToken&&fake.Uri.EndsWith("auth/login")&&fake.Method=="POST","Login cria token a partir de jti");
  fake.Response="{}";try{await api.LoginAsync(mail,"pwd",Convert.ToBase64String(key));Check(false,"Login sem jti recusado");}catch(InvalidOperationException){Check(true,"Login sem jti recusado");}
  var work=P(Operation.DeleteAccounts,list).Items;var recorded=new List<ApiResult>();int calls=0;
  await BatchRunner.RunAsync(work,x=>{calls++;return Task.FromResult(new ApiResult(x.Target,x.Description,"Erro",401,"negado",true));},x=>{recorded.Add(x);return Task.CompletedTask;},()=>false,0);
  Check(calls==1&&recorded[1].Status=="Não enviado","Erro fatal impede próxima chamada");calls=0;recorded.Clear();
  await BatchRunner.RunAsync(work,x=>{calls++;return Task.FromResult(new ApiResult(x.Target,x.Description,"Sucesso",200,"ok"));},x=>{recorded.Add(x);return Task.CompletedTask;},()=>true,0);
  Check(calls==0&&recorded.All(r=>r.Status=="Não enviado"),"Parada antes do lote não envia nada");
  calls=0;try{await BatchRunner.RunAsync(work,x=>{calls++;return Task.FromResult(new ApiResult(x.Target,x.Description,"Sucesso",200,"ok"));},x=>throw new System.IO.IOException(),()=>false,0);}catch(System.IO.IOException){}
  Check(calls==1,"Falha ao gravar relatório impede próxima chamada");
  api.ClearToken();try{await api.ExecuteAsync(item);Check(false,"Sem token não executa");}catch(InvalidOperationException){Check(true,"Sem token não executa");}
  fake.Code=200;fake.Response="{\"data\":{\"jti\":\"abc123\"}}";await api.LoginAsync(mail,"pwd",Convert.ToBase64String(key));
  Check(fake.Agent==ApiClient.UserAgent,"Login envia o cabecalho User-Agent");
  await api.ExecuteAsync(item);
  Check(fake.Agent==ApiClient.UserAgent,"Operacao envia o cabecalho User-Agent");
  using(var exigente=new ApiClient(new SemUserAgent())){
   try{await exigente.LoginAsync(mail,"pwd",Convert.ToBase64String(key));}catch(InvalidOperationException){}
   Check(exigente.HasToken,"Servidor que exige User-Agent aceita o login");
  }
  Check(ApiClient.ExplainLogin(403)!=ApiClient.Explain(403),"Login tem mensagem propria para 403");
  Check(ApiClient.ExplainLogin(401)!=ApiClient.Explain(401),"Login tem mensagem propria para 401");
  Check(ApiClient.ExplainLogin(500)==ApiClient.Explain(500),"Login reaproveita as demais mensagens");
  await AdvancedTests.Run(Check);
  Console.WriteLine($"\nRESULTADO: {passed} passaram; {failed} falharam. Nenhuma chamada real à Skymail.");Environment.ExitCode=failed==0?0:1;
 }
 /// <summary>Imita a exigencia da API real: sem User-Agent devolve 403; com ele, autentica.</summary>
 class SemUserAgent:HttpMessageHandler {
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
   bool temAgente=request.Headers.UserAgent.Count>0;
   return Task.FromResult(new HttpResponseMessage(temAgente?HttpStatusCode.OK:HttpStatusCode.Forbidden){
    Content=new StringContent(temAgente?"{\"data\":{\"jti\":\"abc123\"}}":"{}")});
  }
 }
 class Fake:HttpMessageHandler {public int Code=200;public string Response="{}",Uri="",Method="",Auth="",Body="",Agent="";public bool ThrowTimeout,ThrowNetwork;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){Uri=request.RequestUri!.ToString();Method=request.Method.Method;Auth=request.Headers.Authorization?.ToString()??"";Agent=request.Headers.UserAgent.ToString();Body=request.Content==null?"":await request.Content.ReadAsStringAsync(cancellationToken);if(ThrowTimeout)throw new TaskCanceledException();if(ThrowNetwork)throw new HttpRequestException();return new HttpResponseMessage((HttpStatusCode)Code){Content=new StringContent(Response)};}}
}
