@echo off
rem Renders the Reference Courtyard from its review viewpoints with Unity in batch mode.
rem
rem Usage: close the Unity Editor, then double-click this file (or run it from a terminal).
rem The pictures are written to build\renders\ReferenceCourtyard-*.png and the log to
rem build\renders\review-renders.log.
rem
rem Unity is started WITH a graphics device (no -nographics), because it has to draw. The pictures are
rem Editor renders: the game's shaders, materials and lighting, but the Editor's quality level, nothing
rem animated and no HUD. They are not pictures of the game on a device. See Docs\VisualReferencePrototype.md.
rem
rem It assembles the Reference Courtyard from code first. It creates or updates that scene and
rem Assets\Threefold\Generated. It does not touch any other scene.

setlocal
set "PROJECT=%~dp0..\.."
set "UNITY=%UNITY_EDITOR%"
if "%UNITY%"=="" set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"
set "OUT=%PROJECT%\build\renders"

if not exist "%UNITY%" (
  echo Unity 6000.6.0f1 was not found at:
  echo   %UNITY%
  echo Set the UNITY_EDITOR environment variable to the full path of Unity.exe and run this again.
  exit /b 2
)

rem See build-android.cmd: a lock file that can be moved was left by an interrupted run; it is set aside, not deleted.
set "STALE_LOCK=%PROJECT%\Temp\UnityLockfile.stale-%RANDOM%%RANDOM%"
if exist "%PROJECT%\Temp\UnityLockfile" (
  move "%PROJECT%\Temp\UnityLockfile" "%STALE_LOCK%" >nul 2>&1
  if errorlevel 1 (
    echo The project is open in Unity: Temp\UnityLockfile is in use. Close the Unity Editor that has this
    echo project open, or end a Unity.exe left running by an interrupted build in Task Manager, then run again.
    exit /b 3
  )
  echo Set aside a Unity lock file left by an interrupted run ^(renamed to Temp\UnityLockfile.stale-...^).
)

rem Remove earlier pictures, so an old one is never mistaken for a new one.
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

echo Rendering the review pictures. Unity starts first; this can take several minutes...
start "" /wait "%UNITY%" -batchmode -projectPath "%PROJECT%" ^
  -executeMethod Threefold.EditorTools.ReviewRenders.CaptureReferenceCourtyard ^
  -logFile "%OUT%\review-renders.log"

if errorlevel 1 (
  echo.
  echo RENDERING FAILED. See build\renders\review-renders.log.
  exit /b 1
)

echo.
echo RENDERS WRITTEN: build\renders
dir /b "%OUT%\*.png"
exit /b 0
