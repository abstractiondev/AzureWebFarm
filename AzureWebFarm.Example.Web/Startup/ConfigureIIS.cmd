if "%EMULATED%"=="true" goto Exit

cd %~dp0
echo %date% %time% >>startup.log
echo %date% %time% >>starterror.log
reg add "hku\.default\software\microsoft\windows\currentversion\explorer\user shell folders" /v "Local AppData" /t REG_EXPAND_SZ /d "%~dp0appdata" /f >> startup.log 2>> starterror.log
powershell -ExecutionPolicy Unrestricted "./ConfigureIIS.ps1" >>startup.log 2>> starterror.log

if NOT ERRORLEVEL 0 goto Error

reg add "hku\.default\software\microsoft\windows\currentversion\explorer\user shell folders" /v "Local AppData" /t REG_EXPAND_SZ /d %%USERPROFILE%%\AppData\Local /f >> startup.log 2>> starterror.log
echo %date% %time% >>startup.log
echo %date% %time% >>starterror.log
EXIT /B 0

:Exit
echo Running on Emulator; No action taken.
EXIT /B 0

:Error
reg add "hku\.default\software\microsoft\windows\currentversion\explorer\user shell folders" /v "Local AppData" /t REG_EXPAND_SZ /d %%USERPROFILE%%\AppData\Local /f >> startup.log 2>> starterror.log
echo %date% %time% >>startup.log
echo %date% %time% >>starterror.log
EXIT /B 1
