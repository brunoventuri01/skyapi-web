@echo off
rem Sobe o SkyAPI Web na maquina local para conferencia visual.
rem Atencao: sem a Vercel nao existe /api/skymail, entao so a interface e a
rem demonstracao funcionam. Para testar a API de verdade, use "vercel dev".
setlocal
set RAIZ=%~dp0..
set DOTNET=%RAIZ%\..\.tools\dotnet\dotnet.exe
if not exist "%DOTNET%" set DOTNET=dotnet
"%DOTNET%" run --project "%RAIZ%\Client\SkyAPI.Web.csproj"
endlocal
