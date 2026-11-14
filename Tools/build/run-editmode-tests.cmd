@echo off
rem Runs the project's EditMode tests with Unity in batch mode.
rem
rem Usage: close the Unity Editor, then double-click this file (or run it from a terminal).
rem The results are written to build\tests\editmode-results.xml (NUnit format) and the log to
rem build\tests\editmode-tests.log. A summary is printed at the end.
rem
rem The same script is used by the "Android build" workflow on the self-hosted runner, before the
rem APK is built. The tests create no assets and write only to temporary folders.

setlocal
set "PROJECT=%~dp0..\.."
set "UNITY=%UNITY_EDITOR%"
if "%UNITY%"=="" set "UNITY=C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"
set "OUT=%PROJECT%\build\tests"

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

rem Remove earlier results, so an old file is never mistaken for a new one.
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

echo Running the EditMode tests. Unity starts first; this can take several minutes...
rem No -quit: Unity quits by itself when the tests are done. "start /wait" passes on its exit code.
start "" /wait "%UNITY%" -batchmode -nographics -projectPath "%PROJECT%" -runTests -testPlatform EditMode ^
  -testResults "%OUT%\editmode-results.xml" -logFile "%OUT%\editmode-tests.log"
set "UNITY_EXIT=%ERRORLEVEL%"

if not exist "%OUT%\editmode-results.xml" (
  echo.
  echo TESTS DID NOT RUN: Unity wrote no results ^(exit code %UNITY_EXIT%^). See build\tests\editmode-tests.log.
  exit /b 1
)

rem Summarise the NUnit results: totals, then the name and message of each failed test.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$r = [xml](Get-Content -Raw '%OUT%\editmode-results.xml'); $t = $r.'test-run';" ^
  "Write-Output ('EditMode tests: {0} total, {1} passed, {2} failed, {3} inconclusive, {4} skipped' -f $t.total, $t.passed, $t.failed, $t.inconclusive, $t.skipped);" ^
  "$r.SelectNodes('//test-case[@result=''Failed'']') | ForEach-Object { Write-Output ('FAILED ' + $_.fullname); Write-Output ('  ' + ($_.failure.message.InnerText -replace '\s+', ' ')) }"

if not "%UNITY_EXIT%"=="0" (
  echo.
  echo TESTS FAILED ^(Unity exit code %UNITY_EXIT%^). See build\tests\editmode-results.xml.
  exit /b 1
)

echo.
echo TESTS PASSED
exit /b 0
