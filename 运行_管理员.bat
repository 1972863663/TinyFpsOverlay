@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~dp0bin\Release\net8.0-windows\win-x64\publish\TinyFpsOverlay.exe' -Verb RunAs"
