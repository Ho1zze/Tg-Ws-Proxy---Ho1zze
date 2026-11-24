@echo off
setlocal
echo [TG PROXY Ho1zze] Building executable...

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "WPF=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
set "REFS=System.dll,System.Core.dll,System.Web.Extensions.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Xaml.dll,%WPF%\WindowsBase.dll,%WPF%\PresentationCore.dll,%WPF%\PresentationFramework.dll"

"%CSC%" /nologo /optimize+ /target:winexe /win32manifest:src\app.manifest /win32icon:app.ico /res:logo.png,logo.png /r:%REFS% /out:"TG PROXY Ho1zze.exe" src\Config.cs src\Crypto.cs src\WebSocketClient.cs src\WsPool.cs src\ProxyServer.cs src\AssemblyInfo.cs src\UI\ModernTheme.cs src\UI\MainWindow.cs src\Program.cs

if %ERRORLEVEL% EQU 0 (
    echo [SUCCESS] TG PROXY Ho1zze.exe built successfully!
) else (
    echo [ERROR] Build failed!
)

if "%~1"=="" pause
