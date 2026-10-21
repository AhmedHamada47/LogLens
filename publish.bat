@echo off
echo ========================================
echo  LogLens - Build and Publish
echo ========================================
echo.

echo [1/3] Publishing self-contained release...
dotnet publish -c Release -r win-x64 --self-contained true -o bin\Release\net9.0\win-x64\publish
if %ERRORLEVEL% neq 0 (
    echo ERROR: Publish failed!
    pause
    exit /b 1
)

echo.
echo [2/3] Building installer...
set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
    echo Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup
    echo Then run this script again.
    pause
    exit /b 1
)
"%ISCC%" installer\LogLens.iss
if %ERRORLEVEL% neq 0 (
    echo ERROR: Installer build failed!
    pause
    exit /b 1
)

echo.
echo [3/3] Done!
echo.
echo Installer: installer\output\LogLens-Setup.exe
echo.
pause
