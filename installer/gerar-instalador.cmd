@echo off
chcp 65001 >nul
setlocal
rem Builds the RemapUSB installer with a double-click:
rem   1. publishes the app with the win-x64 profile (installer\publish)
rem   2. compiles installer\RemapUSB.iss with Inno Setup 6 (installer\Output)
rem Messages shown on screen are in Brazilian Portuguese, like the app.

cd /d "%~dp0.."

echo.
echo [1/2] Publicando o app (perfil win-x64)...
dotnet publish src\RemapUSB.App -p:PublishProfile=win-x64
if errorlevel 1 goto falhou

rem Usual Inno Setup 6 install locations (for all users or for the current user only).
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
