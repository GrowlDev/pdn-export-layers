@echo off
:: Installs the ExportLayers plugin into the per-user Paint.NET plugin folder
:: (scanned automatically by Paint.NET 5.x on classic installs; no admin needed).
:: Close Paint.NET first if it is running, then restart it after installing.

set SRC=%~dp0ExportLayers\bin\Release\ExportLayers.dll
set DST=%USERPROFILE%\Documents\Paint.NET App Files\Effects

if not exist "%SRC%" (
    echo Build output not found: %SRC%
    echo Build first:  dotnet build -c Release  (in the ExportLayers folder)
    exit /b 1
)

if not exist "%DST%" mkdir "%DST%"
copy /y "%SRC%" "%DST%\ExportLayers.dll"
if errorlevel 1 (
    echo Copy failed. Is Paint.NET still running?
    exit /b 1
)

echo.
echo Installed to "%DST%\ExportLayers.dll".
echo Start (or restart) Paint.NET and look in: Effects ^> Tools ^> Export Layers to PNGs...
