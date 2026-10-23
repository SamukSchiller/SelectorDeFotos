@echo off
rem Compila SelectorDeFotos.exe con el compilador de C# incluido en Windows (.NET Framework 4.8).
rem No necesita instalar nada.
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
set OUT=%~1
if "%OUT%"=="" set OUT=SelectorDeFotos.exe
cd /d "%~dp0"
"%FW%\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu ^
  /out:"%OUT%" ^
  /win32icon:icono.ico ^
  /resource:icono.ico,SelectorDeFotos.icono.ico ^
  /r:"%FW%\WPF\PresentationCore.dll" ^
  /r:"%FW%\WPF\PresentationFramework.dll" ^
  /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" ^
  /r:System.Web.Extensions.dll ^
  /r:System.Core.dll ^
  /r:Microsoft.VisualBasic.dll ^
  /recurse:src\*.cs
if errorlevel 1 (echo. & echo ERROR de compilacion & exit /b 1)
echo Compilado: %OUT%
