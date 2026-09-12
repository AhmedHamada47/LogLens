@echo off
echo ========================================
echo  LogLens - Build and Publish
echo ========================================
echo.

echo [1/2] Publishing self-contained release...
dotnet publish -c Release -r win-x64 --self-contained true -o bin\Release\net9.0\win-x64\publish
if %ERRORLEVEL% neq 0 (
    echo ERROR: Publish failed!
    pause
    exit /b 1
)

echo.
echo [2/2] Publish complete!
echo.
echo Output folder: bin\Release\net9.0\win-x64\publish\
echo.
echo Next steps:
echo   1. Install Inno Setup from https://jrsoftware.org/isdl.php
echo   2. Open installer\LogLens.iss in Inno Setup
echo   3. Click Build ^> Compile
echo   4. Find LogLens-Setup.exe in installer\output\
echo.
pause
