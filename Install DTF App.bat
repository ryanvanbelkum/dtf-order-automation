@echo off
setlocal EnableDelayedExpansion

title DTF Order Automation - Installing...

:: ── write log to Desktop ────────────────────────────────────────────────────
set "LOG=%USERPROFILE%\Desktop\dtf_install_log.txt"
echo DTF Installer started: %DATE% %TIME% > "%LOG%"
echo Running from: %~dp0 >> "%LOG%"

:: ── set working directory ────────────────────────────────────────────────────
cd /d "%~dp0"
echo Working directory: %CD% >> "%LOG%"

echo.
echo  ================================================
echo    DTF Order Automation - One-Time Setup
echo  ================================================
echo.
echo  This will take 2-3 minutes. Please don't close
echo  this window until you see "All done!"
echo.

:: ── step 1: Python ──────────────────────────────────────────────────────────
echo  [1/4] Checking for Python...
echo Step 1: Python >> "%LOG%"
python --version >> "%LOG%" 2>&1

if errorlevel 1 (
    echo        Not found. Downloading Python...
    echo Python not found, downloading... >> "%LOG%"

    set "PY_URL=https://www.python.org/ftp/python/3.12.3/python-3.12.3-amd64.exe"
    set "PY_INSTALLER=%TEMP%\python_installer.exe"

    powershell -Command "(New-Object System.Net.WebClient).DownloadFile('!PY_URL!', '!PY_INSTALLER!')" >> "%LOG%" 2>&1
    if not exist "!PY_INSTALLER!" (
        curl -L -o "!PY_INSTALLER!" "!PY_URL!" >> "%LOG%" 2>&1
    )
    if not exist "!PY_INSTALLER!" (
        echo FAILED: Python download >> "%LOG%"
        echo.
        echo  ERROR: Could not download Python.
        echo  Check dtf_install_log.txt on your Desktop for details.
        echo.
        pause
        exit /b 1
    )

    echo        Installing Python...
    "!PY_INSTALLER!" /quiet InstallAllUsers=0 PrependPath=1 Include_test=0
    echo Python installer finished >> "%LOG%"

    :: refresh PATH so Python is usable right away
    for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v PATH 2^>nul') do set "PATH=%%b;%PATH%"
    for /f "tokens=2*" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v PATH 2^>nul') do set "PATH=!PATH!;%%b"

    python --version >> "%LOG%" 2>&1
    if errorlevel 1 (
        echo FAILED: Python still not found after install >> "%LOG%"
        echo.
        echo  ERROR: Python installed but still not found.
        echo  Please close this window and run the installer again.
        echo.
        pause
        exit /b 1
    )
    echo        Python installed OK.
) else (
    echo        Python found.
)
echo Python OK >> "%LOG%"
echo.

:: ── step 2: dependencies ────────────────────────────────────────────────────
echo  [2/4] Installing dependencies...
echo Step 2: pip >> "%LOG%"

python -m pip install --upgrade pip >> "%LOG%" 2>&1
python -m pip install pillow pystray openpyxl pyinstaller >> "%LOG%" 2>&1
if errorlevel 1 (
    echo FAILED: pip install >> "%LOG%"
    echo.
    echo  ERROR: Failed to install dependencies.
    echo  Check dtf_install_log.txt on your Desktop for details.
    echo.
    pause
    exit /b 1
)
echo        Done.
echo pip OK >> "%LOG%"
echo.

:: ── step 3: build exe ───────────────────────────────────────────────────────
echo  [3/4] Building app (takes about 2 minutes)...
echo Step 3: PyInstaller >> "%LOG%"

set "SCRIPT_DIR=%~dp0"
set "DIST_DIR=%SCRIPT_DIR%dist"

python -m PyInstaller ^
    --onefile ^
    --windowed ^
    --name "DTF Order Automation" ^
    --distpath "%DIST_DIR%" ^
    --workpath "%TEMP%\dtf_build" ^
    --specpath "%TEMP%\dtf_build" ^
    "%SCRIPT_DIR%dtf_app.py" >> "%LOG%" 2>&1

if not exist "%DIST_DIR%\DTF Order Automation.exe" (
    echo FAILED: exe not produced >> "%LOG%"
    echo.
    echo  ERROR: Build failed.
    echo  Check dtf_install_log.txt on your Desktop for details.
    echo.
    pause
    exit /b 1
)
echo        App built.
echo PyInstaller OK >> "%LOG%"
echo.

:: ── step 4: desktop shortcut ────────────────────────────────────────────────
echo  [4/4] Creating desktop shortcut...
echo Step 4: shortcut >> "%LOG%"

set "EXE=%DIST_DIR%\DTF Order Automation.exe"
set "SHORTCUT=%USERPROFILE%\Desktop\DTF Order Automation.lnk"

powershell -Command "$ws=New-Object -ComObject WScript.Shell; $s=$ws.CreateShortcut('%SHORTCUT%'); $s.TargetPath='%EXE%'; $s.WorkingDirectory='%DIST_DIR%'; $s.Save()" >> "%LOG%" 2>&1

if exist "%SHORTCUT%" (
    echo        Shortcut created on your Desktop.
    echo Shortcut OK >> "%LOG%"
) else (
    echo        Shortcut failed - drag the .exe to your Desktop manually.
    echo Shortcut FAILED >> "%LOG%"
)
echo.

echo Completed: %DATE% %TIME% >> "%LOG%"

:: ── done ────────────────────────────────────────────────────────────────────
echo  ================================================
echo    All done!
echo  ================================================
echo.
echo  Open "DTF Order Automation" from your Desktop.
echo  Go to Settings to enter your Shopify and folder details.
echo.
explorer "%DIST_DIR%"
pause
exit /b 0
