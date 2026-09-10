@echo off
rem Compila o SkyAPI Web na maquina local, do mesmo jeito que a Vercel compila.
rem Resultado estatico em WEB\dist. Use para conferir antes de publicar.
setlocal
set RAIZ=%~dp0..
set DOTNET=%RAIZ%\..\.tools\dotnet\dotnet.exe
if not exist "%DOTNET%" set DOTNET=dotnet

if exist "%RAIZ%\publish" rmdir /s /q "%RAIZ%\publish"
if exist "%RAIZ%\dist" rmdir /s /q "%RAIZ%\dist"

"%DOTNET%" publish "%RAIZ%\Client\SkyAPI.Web.csproj" -c Release -o "%RAIZ%\publish"
if errorlevel 1 goto :erro

move "%RAIZ%\publish\wwwroot" "%RAIZ%\dist" >nul
rmdir /s /q "%RAIZ%\publish"
echo.
echo Pronto. Arquivos estaticos em: %RAIZ%\dist
goto :fim

:erro
echo.
echo A compilacao falhou. Nenhum arquivo foi publicado.
exit /b 1

:fim
endlocal
