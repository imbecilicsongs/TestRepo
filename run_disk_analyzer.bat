@echo off
:: Disk Space Analyzer Launcher
:: Requires Python 3.6+ installed (https://python.org)

where python >nul 2>&1
if errorlevel 1 (
    echo Python not found. Please install Python 3.6+ from https://python.org
    pause
    exit /b 1
)

python "%~dp0disk_analyzer.py"
if errorlevel 1 (
    echo.
    echo Error running Disk Space Analyzer.
    pause
)
