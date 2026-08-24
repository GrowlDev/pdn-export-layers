@echo off
:: Removes the ExportLayers plugin. Close Paint.NET first.
del "%USERPROFILE%\Documents\Paint.NET App Files\Effects\ExportLayers.dll"
if errorlevel 1 (
    echo Delete failed. Close Paint.NET and try again.
    exit /b 1
)
echo Uninstalled.
