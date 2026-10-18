@echo off
setlocal
rem Builds DualSensePad.dll with the C# compiler that ships with Windows (.NET Framework 4.x)
rem against the game's own (Unity 5 / Mono 2.0) libraries, then copies it into the Mods folder.

cd /d "%~dp0"
set "GAME=%~dp0.."
if defined MWC_DIR set "GAME=%MWC_DIR%"
set "MANAGED=%GAME%\mywintercar_Data\Managed"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. > build_log.txt
  type build_log.txt
  pause
  exit /b 1
)
if not exist bin mkdir bin

"%CSC%" /nologo /noconfig /nostdlib+ /target:library /optimize+ /langversion:5 /utf8output /codepage:65001 ^
 /out:"bin\DualSensePad.dll" ^
 /reference:"%MANAGED%\mscorlib.dll" ^
 /reference:"%MANAGED%\System.dll" ^
 /reference:"%MANAGED%\System.Core.dll" ^
 /reference:"%MANAGED%\UnityEngine.dll" ^
 /reference:"%MANAGED%\PlayMaker.dll" ^
 /reference:"%MANAGED%\Assembly-CSharp.dll" ^
 /reference:"%MANAGED%\cInput.dll" ^
 /reference:"%MANAGED%\0Harmony.dll" ^
 /reference:"%MANAGED%\MSCLoader.dll" ^
 src\*.cs > build_log.txt 2>&1

if errorlevel 1 (
  echo BUILD FAILED >> build_log.txt
  type build_log.txt
  pause
  exit /b 1
)

copy /y "bin\DualSensePad.dll" "%GAME%\Mods\DualSensePad.dll" >> build_log.txt
echo BUILD OK - copied to Mods\DualSensePad.dll >> build_log.txt
type build_log.txt
pause
