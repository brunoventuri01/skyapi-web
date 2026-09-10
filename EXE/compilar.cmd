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
copy /y release\SkyAPI.exe SkyAPI-1.1.12.exe >nul
if errorlevel 1 (
  echo.
  echo Nao foi possivel gravar SkyAPI-1.1.12.exe.
  echo O arquivo esta em uso: feche o SkyAPI se ele estiver aberto e rode de novo.
  echo O build novo continua em release\SkyAPI.exe.
  pause
  exit /b 1
)

rem Sem assinatura o executavel recem compilado nao abre com o Smart App Control ligado.
rem O certificado padrao e autoassinado: serve para testar aqui, nao para distribuir.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\assinar.ps1" -Arquivo "%~dp0SkyAPI-1.1.12.exe"
if errorlevel 1 echo AVISO: o executavel ficou sem assinatura.

echo Concluido: SkyAPI-1.1.12.exe
pause
