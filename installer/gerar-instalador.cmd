@echo off
chcp 65001 >nul
setlocal
rem Gera o instalador do RemapUSB com dois cliques:
rem   1. publica o app com o perfil win-x64 (installer\publish)
rem   2. compila o installer\RemapUSB.iss com o Inno Setup 6 (installer\Output)

cd /d "%~dp0.."

echo.
echo [1/2] Publicando o app (perfil win-x64)...
dotnet publish src\RemapUSB.App -p:PublishProfile=win-x64
if errorlevel 1 goto falhou

rem Lugares em que o Inno Setup 6 costuma ser instalado (para todos ou só para o usuário).
set "ISCC="
for %%P in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe") do (
  if not defined ISCC if exist "%%~P" set "ISCC=%%~P"
)
if not defined ISCC for /f "delims=" %%P in ('where ISCC.exe 2^>nul') do if not defined ISCC set "ISCC=%%P"
if not defined ISCC goto semInno

echo.
echo [2/2] Compilando o instalador...
"%ISCC%" "installer\RemapUSB.iss"
if errorlevel 1 goto falhou

echo.
echo Pronto. Instalador gerado em installer\Output:
dir /b "installer\Output\*.exe"
start "" explorer "%CD%\installer\Output"
echo.
pause
exit /b 0

:semInno
echo.
echo Não achei o Inno Setup 6 (ISCC.exe). Instale em https://jrsoftware.org/isinfo.php e rode de novo.

:falhou
echo.
echo Falhou. Veja a mensagem acima.
pause
exit /b 1
