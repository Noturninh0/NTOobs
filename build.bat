@echo off
setlocal
cd /d "%~dp0"

echo === NTOobs: gerando o executavel ===
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo O .NET SDK 8 nao foi encontrado. Tentando instalar com o winget...
    winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
    echo.
    echo Instalacao concluida. FECHE esta janela e de dois cliques em build.bat de novo.
    pause
    exit /b 1
)

dotnet publish NTOobs\NTOobs.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
if errorlevel 1 (
    echo.
    echo O build falhou. Copie as mensagens de erro acima e me envie.
    pause
    exit /b 1
)

echo.
echo Pronto! O executavel esta em: %~dp0dist\NTOobs.exe
echo (nao precisa ter o .NET instalado para abrir esse arquivo)
start "" "%~dp0dist\NTOobs.exe"
pause
