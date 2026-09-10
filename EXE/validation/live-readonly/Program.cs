using SkyAPI.Core;
using var api=new ApiClient();
var username=Console.ReadLine()??"";
var password=Console.ReadLine()??"";
var key=Console.ReadLine()??"";
await api.LoginAsync(username,password,key);
username=password=key="";
var service=new AdvancedService(api);
var products=await service.ClientProducts();
Console.WriteLine("Consulta real: "+products.Select(p=>p.ClientId).Distinct().Count()+" cliente(s), "+products.Count+" produtos de caixa.");
foreach(var item in products) {
 var stock=await service.ClientProductBalance(item.ClientId,item.Product);
 if(stock==null)throw new Exception("Saldo indisponível para produto retornado.");
 Console.WriteLine(item.Product.Name+": saldo consultado.");
}
api.ClearToken();
Console.WriteLine("Somente autenticação e GET; nenhuma alteração enviada.");
