using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.NET.HostModel.AppHost;
using Microsoft.NET.HostModel.Bundle;
class BuildBundle{
 static void Main(string[] args){string stage=args[0],output=args[1],host=args[2];Directory.CreateDirectory(output);HostWriter.CreateAppHost(host,Path.Combine(stage,"SkyAPI.exe"),"SkyAPI.dll",true,Path.Combine(stage,"SkyAPI.dll"));var bundler=new Bundler("SkyAPI.exe",output,BundleOptions.BundleAllContent|BundleOptions.EnableCompression,OSPlatform.Windows,Architecture.X64,new Version(8,0),false,"SkyAPI");var files=Directory.GetFiles(stage,"*",SearchOption.AllDirectories).Select(p=>new FileSpec(p,Path.GetRelativePath(stage,p))).ToArray();string path=bundler.GenerateBundle(files);Console.WriteLine(path);Console.WriteLine("Embedded files: "+files.Length);Console.WriteLine("Bundle verified: "+HostWriter.IsBundle(path,out var offset)+"; header offset: "+offset);}
}
