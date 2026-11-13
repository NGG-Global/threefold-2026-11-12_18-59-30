@echo off
rem Builds a development APK of the playable prototype with Unity in batch mode.
rem
rem Usage: close the Unity Editor, then double-click this file (or run it from a terminal).
rem The APK is written to build\Android\Threefold.apk and the log to build\android-build.log.
rem
rem The build assembles the Fantasy Playground scene from code first. It creates or updates
rem Assets\Scenes\FantasyPlayground.unity and Assets\Threefold\Generated. It does not touch any
rem other scene.
rem
rem The same script is used by the "Android build" workflow on a self-hosted runner.
rem It has not been run yet; see Docs\CloudBuild.md.

setlocal
set "PROJECT=%~dp0..\.."
set "UNITY=%UNITY_EDITOR%"
if "%UNITY%"=="" set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"

if not exist "%UNITY%" (
  echo Unity 6000.6.0f1 was not found at:
  echo   %UNITY%
  echo Set the UNITY_EDITOR environment variable to the full path of Unity.exe and run this again.
  exit /b 2
)

if exist "%PROJECT%\Temp\UnityLockfile" (
  echo The project is open in the Unity Editor. Close Unity and run this again.
  exit /b 3
)

if not exist "%PROJECT%\build" mkdir "%PROJECT%\build"

rem Remove the results of any earlier build, so an old APK or log is never mistaken for a new one.
if exist "%PROJECT%\build\Android" rmdir /s /q "%PROJECT%\build\Android"
if exist "%PROJECT%\build\logs" rmdir /s /q "%PROJECT%\build\logs"
if exist "%PROJECT%\build\android-build.log" del /q "%PROJECT%\build\android-build.log"

echo Building. This can take a long time on the first run...
rem "start /wait" makes the script wait for Unity and receive its exit code.
start "" /wait "%UNITY%" -batchmode -nographics -projectPath "%PROJECT%" -buildTarget Android ^
  -executeMethod Threefold.EditorTools.PrototypeBuild.BuildAndroid ^
  -customBuildPath "%PROJECT%\build\Android\Threefold.apk" ^
  -logFile "%PROJECT%\build\android-build.log"

if errorlevel 1 (
  echo.
  echo BUILD FAILED. Send the file build\android-build.log to Claude Code.
  exit /b 1
)

if not exist "%PROJECT%\build\Android\Threefold.apk" (
  echo.
  echo BUILD FAILED: Unity reported success but no APK was written. See build\android-build.log.
  exit /b 1
)

echo.
echo BUILD SUCCEEDED: build\Android\Threefold.apk
exit /b 0
