using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace SkyAPI.Core;
public static class BatchRunner {
    public static async Task RunAsync(IReadOnlyList<WorkItem> items,Func<WorkItem,Task<ApiResult>> execute,Func<ApiResult,Task> record,Func<bool> stop,int delayMilliseconds=150){
        bool halted=false;
        foreach(var item in items){
            ApiResult result;
            if(halted||stop())result=new(item.Target,item.Description,"Não enviado",null,"Lote interrompido antes deste registro.");
            else result=await execute(item);
            await record(result); // Falha ao registrar interrompe imediatamente os próximos envios.
            if(result.StopBatch)halted=true;
            if(delayMilliseconds>0&&!halted&&!stop())await Task.Delay(delayMilliseconds);
        }
    }
}
