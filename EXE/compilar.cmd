@echo off
setlocal
cd /d "%~dp0"
set "DOTNET=dotnet"
if exist "..\.tools\dotnet\dotnet.exe" set "DOTNET=%~dp0..\.tools\dotnet\dotnet.exe"
if exist ".tools\dotnet\dotnet.exe" set "DOTNET=%~dp0.tools\dotnet\dotnet.exe"
"%DOTNET%" --version >nul 2>nul
if errorlevel 1 (
  echo Instale o SDK .NET 8 atualizado para compilar.
  pause
  exit /b 1
)
"%DOTNET%" publish src\SkyAPI.Desktop\SkyAPI.Desktop.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o release
if errorlevel 1 (
  echo A compilacao falhou. Verifique a mensagem acima.
  pause
  exit /b 1
)
copy /y release\SkyAPI.exe SkyAPI-1.1.11.exe >nul
echo Concluido: SkyAPI-1.1.11.exe
pause
