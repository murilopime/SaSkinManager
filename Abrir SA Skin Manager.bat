@echo off
chcp 65001 >nul
title SA Skin Manager
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo O .NET 8 SDK nao esta instalado.
    echo Baixe em: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

set "OUT=src\SaSkinManager.App\bin\Release\net8.0-windows"

rem Se o programa ja estiver aberto (mesmo sem janela), ele trava a compilacao.
tasklist /fi "imagename eq SaSkinManager.exe" | find /i "SaSkinManager.exe" >nul
if not errorlevel 1 (
    echo O SA Skin Manager ja esta aberto.
    echo Ele precisa ser fechado para atualizar. Se estiver no meio de uma troca, espere terminar.
    choice /c SN /m "Fechar e reabrir agora"
    if errorlevel 2 exit /b 0
    taskkill /im SaSkinManager.exe /f >nul 2>nul
    ping -n 2 127.0.0.1 >nul
)

echo Compilando o SA Skin Manager...
dotnet build "src\SaSkinManager.App\SaSkinManager.App.csproj" -c Release -nologo -v quiet
if errorlevel 1 (
    echo.
    echo Erro ao compilar. Veja as mensagens acima.
    pause
    exit /b 1
)

rem Tenta abrir o .exe; se o Windows bloquear, abre pelo dotnet.
start "" "%OUT%\SaSkinManager.exe"
ping -n 4 127.0.0.1 >nul
tasklist /fi "imagename eq SaSkinManager.exe" | find /i "SaSkinManager.exe" >nul
if errorlevel 1 (
    echo O .exe nao abriu, abrindo pelo dotnet...
    start "SA Skin Manager" /min dotnet "%OUT%\SaSkinManager.dll"
)
