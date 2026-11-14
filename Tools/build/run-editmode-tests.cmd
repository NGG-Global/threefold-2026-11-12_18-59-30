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

rem A Unity Editor that has this project open keeps Temp\UnityLockfile open, and Windows then refuses to
rem move it. If it can be moved, no Unity has the project open: it was left behind by a run that was cut
rem off (a shutdown, a restart, a stopped runner). It is set aside under another name, not deleted.
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

rem Summarise the NUnit results: totals, each failed test with its message, and each inconclusive test
rem (for example the allocation tests, which Unity's runtime cannot measure). The result decides from the
rem count of failed tests: Unity also returns exit code 2 when a test is only inconclusive. PowerShell
rem exits with 1 when a test failed or the results cannot be read.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$r = [xml](Get-Content -Raw '%OUT%\editmode-results.xml'); $t = $r.'test-run';" ^
  "Write-Output ('EditMode tests: {0} total, {1} passed, {2} failed, {3} inconclusive, {4} skipped' -f $t.total, $t.passed, $t.failed, $t.inconclusive, $t.skipped);" ^
  "$r.SelectNodes('//test-case[@result=''Failed'']') | ForEach-Object { Write-Output ('FAILED ' + $_.fullname); Write-Output ('  ' + ($_.SelectSingleNode('failure/message').InnerText -replace '\s+', ' ')) };" ^
  "$r.SelectNodes('//test-case[@result=''Inconclusive'']') | ForEach-Object { Write-Output ('INCONCLUSIVE ' + $_.fullname) };" ^
  "if ([int]$t.failed -gt 0 -or [int]$t.total -eq 0) { exit 1 } else { exit 0 }"
set "TESTS_FAILED=%ERRORLEVEL%"

if not "%TESTS_FAILED%"=="0" (
  echo.
  echo TESTS FAILED ^(Unity exit code %UNITY_EXIT%^). See build\tests\editmode-results.xml.
  exit /b 1
)

rem 0: all passed. 2: Unity's code for a run with failed or inconclusive tests; none failed, as checked above.
rem Anything else means the run itself went wrong.
if not "%UNITY_EXIT%"=="0" if not "%UNITY_EXIT%"=="2" (
  echo.
  echo TESTS DID NOT COMPLETE ^(Unity exit code %UNITY_EXIT%^). See build\tests\editmode-tests.log.
  exit /b 1
)

echo.
echo TESTS PASSED ^(no test failed; any inconclusive tests are listed above^)
exit /b 0
