@echo off
setlocal EnableExtensions DisableDelayedExpansion
cd /d "%~dp0"
call "%~dp0RUN_SPINDOCTOR.cmd" %*
exit /b %ERRORLEVEL%
