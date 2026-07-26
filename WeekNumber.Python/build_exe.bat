@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

:: ============================================================
::  WeekNumber build - the only build file in this project.
::
::  Usage:  build_exe.bat [--onedir] [--venv] [--nopause]
::    (default)  one-file dist\WeekNumber.exe
::    --onedir   folder build dist\WeekNumber\ - starts faster at
::               logon (no per-launch %TEMP% extraction / AV rescan)
::    --venv     build inside an isolated .venv-build with pinned
::               deps: smallest, reproducible exe regardless of
::               what is installed globally
::    --nopause  no interactive pause; returns exit code 0/1
::
::  The PyInstaller spec and the VERSIONINFO resource are
::  generated into build\ on every run; the version number is
::  read from APP_VERSION in weeknumber_core.py.
:: ============================================================

set "BUILD_OK=0"
set "PY="
set "MODE=--onefile"
set "OUTEXE=dist\WeekNumber.exe"
set "NOPAUSE=0"
set "USEVENV=0"

for %%A in (%*) do (
    if /i "%%~A"=="--nopause" set "NOPAUSE=1"
    if /i "%%~A"=="--venv"    set "USEVENV=1"
    if /i "%%~A"=="--onedir" (
        set "MODE="
        set "OUTEXE=dist\WeekNumber\WeekNumber.exe"
    )
)

echo ============================================================
echo  WeekNumber EXE build
echo ============================================================
echo.

:: ------------------------------------------------------------
:: [1/8] Check required files
:: ------------------------------------------------------------
echo [1/8] Checking project files...
echo       Folder: !CD!

for %%F in (weeknumber.py weeknumber_core.py weeknumber_tray.py weeknumber_about.py weeknumber_calendar.py weeknumber_clipboard.py holiday_data.json) do (
    if not exist "%%F" (
        echo       ERROR: %%F not found.
        goto :DONE
    )
)

echo       OK

:: ------------------------------------------------------------
:: [2/8] Check optional icon
:: ------------------------------------------------------------
echo.
echo [2/8] Checking optional app.ico...

set "ICON_ARG="
set "ICON_DATA="
if exist "app.ico" (
    set "ICON_ARG=--icon "!CD!\app.ico""
    set "ICON_DATA=--add-data "!CD!\app.ico;.""
    echo       OK - app.ico found; used as the EXE icon and bundled for the
    echo       About card.
) else (
    echo       OK - app.ico not found; default EXE icon, About card without
    echo       an icon.
)

:: ------------------------------------------------------------
:: [3/8] Detect Python
::   `where python` alone is not enough: the Microsoft Store
::   alias is a python.exe on PATH that only prints an install
::   hint. Validate by executing, fall back to the py launcher.
:: ------------------------------------------------------------
echo.
echo [3/8] Detecting Python...

python -c "import sys" >nul 2>nul
if not errorlevel 1 (
    set "PY=python"
) else (
    py -3 -c "import sys" >nul 2>nul
    if not errorlevel 1 set "PY=py -3"
)

if not defined PY (
    echo       ERROR: No working Python found. Checked: python, py -3.
    echo       Note: a PATH entry alone can be the Microsoft Store alias,
    echo       which is not a real interpreter.
    goto :DONE
)

for /f "delims=" %%V in ('!PY! --version 2^>nul') do set "PY_VERSION=%%V"
echo       OK - !PY_VERSION! via "!PY!"

:: ------------------------------------------------------------
:: [4/8] Isolated build venv (--venv)
:: ------------------------------------------------------------
echo.
echo [4/8] Build environment...

if "%USEVENV%"=="0" (
    echo       Using the detected Python environment as-is.
    echo       Hint: --venv builds in a clean pinned venv - smallest,
    echo       reproducible exe independent of global site-packages.
    goto :ENV_DONE
)

if not exist ".venv-build\Scripts\python.exe" (
    echo       Creating .venv-build...
    !PY! -m venv .venv-build
    if errorlevel 1 (
        echo       ERROR: venv creation failed.
        goto :DONE
    )
    echo       Installing pinned build dependencies...
    .venv-build\Scripts\python.exe -m pip install --quiet --upgrade pip
    .venv-build\Scripts\python.exe -m pip install --quiet "pyinstaller>=6.6,<7" "pillow>=10.1" "pystray>=0.19.5"
    if errorlevel 1 (
        echo       ERROR: dependency install failed. Delete .venv-build and retry.
        goto :DONE
    )
)
set "PY=.venv-build\Scripts\python.exe"
echo       OK - using .venv-build

:ENV_DONE

:: ------------------------------------------------------------
:: [5/8] Check PyInstaller (needs >= 6.6 for --optimize)
:: ------------------------------------------------------------
echo.
echo [5/8] Checking PyInstaller...

!PY! -c "import PyInstaller, sys; v = tuple(int(x) for x in PyInstaller.__version__.split('.')[:2]); sys.exit(0 if v >= (6, 6) else 1)" >nul 2>nul
if errorlevel 1 (
    echo       ERROR: PyInstaller 6.6+ not available in this environment.
    echo.
    echo       Fix, either:
    echo         build_exe.bat --venv
    echo       or:
    echo         !PY! -m pip install "pyinstaller>=6.6,<7" "pillow>=10.1" "pystray>=0.19.5"
    goto :DONE
)

for /f "delims=" %%V in ('!PY! -m PyInstaller --version 2^>nul') do set "PYI_VERSION=%%V"
echo       OK - PyInstaller !PYI_VERSION!

:: ------------------------------------------------------------
:: [6/8] Clean old build, generate the VERSIONINFO resource
:: ------------------------------------------------------------
echo.
echo [6/8] Cleaning and preparing build\ ...

if exist "build" rmdir /s /q "build"
if exist "dist"  rmdir /s /q "dist"

if exist "%OUTEXE%" (
    echo       ERROR: %OUTEXE% is locked - a WeekNumber started from
    echo       dist\ is still running. Exit it via the tray menu, then
    echo       rebuild.
    goto :DONE
)

mkdir "build"

:: Version = APP_VERSION in weeknumber_core.py (single source of truth).
:: Classic findstr idiom: the line's two quotes make the version token 2.
set "APPVER="
for /f tokens^=2^ delims^=^" %%v in ('findstr /r /c:"^APP_VERSION" weeknumber_core.py') do set "APPVER=%%v"

if not defined APPVER (
    echo       ERROR: could not read APP_VERSION from weeknumber_core.py.
    goto :DONE
)
set "QUADVER=%APPVER%.0.0"
set "QUADVER_TUPLE=%APPVER:.=, %, 0, 0"
echo       App version: %APPVER%  (resource %QUADVER%)

:: Absolute: with --specpath, PyInstaller resolves relative file
:: arguments against the spec directory, not the CWD.
set "VERFILE=!CD!\build\version_info.txt"
> "!VERFILE!" echo # Generated by build_exe.bat - do not edit; source is APP_VERSION.
>>"!VERFILE!" echo VSVersionInfo(
>>"!VERFILE!" echo   ffi=FixedFileInfo(
>>"!VERFILE!" echo     filevers=(%QUADVER_TUPLE%),
>>"!VERFILE!" echo     prodvers=(%QUADVER_TUPLE%),
>>"!VERFILE!" echo     mask=0x3F, flags=0x0, OS=0x40004, fileType=0x1, subtype=0x0, date=(0, 0),
>>"!VERFILE!" echo   ),
>>"!VERFILE!" echo   kids=[
>>"!VERFILE!" echo     StringFileInfo([
>>"!VERFILE!" echo       StringTable(
>>"!VERFILE!" echo         "040904B0",
>>"!VERFILE!" echo         [
>>"!VERFILE!" echo           StringStruct("FileDescription", "WeekNumber - ISO week number in the system tray"),
>>"!VERFILE!" echo           StringStruct("FileVersion", "%QUADVER%"),
>>"!VERFILE!" echo           StringStruct("InternalName", "WeekNumber"),
>>"!VERFILE!" echo           StringStruct("OriginalFilename", "WeekNumber.exe"),
>>"!VERFILE!" echo           StringStruct("ProductName", "WeekNumber"),
>>"!VERFILE!" echo           StringStruct("ProductVersion", "%QUADVER%"),
>>"!VERFILE!" echo         ],
>>"!VERFILE!" echo       )
>>"!VERFILE!" echo     ]),
>>"!VERFILE!" echo     VarFileInfo([VarStruct("Translation", [1033, 1200])]),
>>"!VERFILE!" echo   ],
>>"!VERFILE!" echo )

echo       OK

:: ------------------------------------------------------------
:: [7/8] Build
::   --optimize 2   strip asserts+docstrings from bytecode; the
::                  app uses neither at runtime (verified)
::   --noupx        UPX would shrink the exe but raises AV
::                  false-positive rates; a flagged exe is lost
::                  functionality
::   --specpath     PyInstaller always writes a generated spec;
::                  point it into build\ so the project root
::                  keeps exactly one build file (this script)
::   excludes       env-pollution insurance only (Pillow probes
::                  Qt); no stdlib excludes - saving ~0.2 MB is
::                  not worth a lazy-import break
:: ------------------------------------------------------------
echo.
echo [7/8] Building executable...

!PY! -m PyInstaller --clean --noconfirm %MODE% --windowed --name WeekNumber ^
    !ICON_ARG! ^
    --add-data "!CD!\holiday_data.json;." ^
    !ICON_DATA! ^
    --version-file "!VERFILE!" ^
    --optimize 2 --noupx --specpath build ^
    --exclude-module PIL.ImageQt ^
    --exclude-module PyQt5 --exclude-module PyQt6 ^
    --exclude-module PySide2 --exclude-module PySide6 ^
    --exclude-module IPython --exclude-module matplotlib ^
    --exclude-module numpy --exclude-module pandas ^
    weeknumber.py

if errorlevel 1 (
    echo       ERROR: Build failed.
    goto :DONE
)

echo       OK

:: ------------------------------------------------------------
:: [8/8] Verify output
:: ------------------------------------------------------------
echo.
echo [8/8] Verifying output...

if not exist "%OUTEXE%" (
    echo       ERROR: %OUTEXE% not found.
    goto :DONE
)

for %%F in ("%OUTEXE%") do (
    echo       OK - %%~fF
    echo       Size: %%~zF bytes
)

set "BUILD_OK=1"

:: ------------------------------------------------------------
:DONE
echo.
echo ============================================================

if "%BUILD_OK%"=="1" (
    echo  SUCCESS: Build completed.
    echo  Output: %OUTEXE%   x64, Windows 10 1703+
    echo.
    echo  Recommendation:
    echo  - Code-sign the EXE for trust with SmartScreen
    echo  - Install under Program Files for proper ACL protection
) else (
    echo  FAILED: See errors above.
)

echo ============================================================
echo.
if "%NOPAUSE%"=="0" pause

if "%BUILD_OK%"=="1" (exit /b 0) else (exit /b 1)
