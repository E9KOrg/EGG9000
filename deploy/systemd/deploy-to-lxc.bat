@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "CONTAINER_IP=192.168.0.190"
set "REMOTE_USER=root"
set "REMOTE_INCOMING=/opt/egg9000/incoming"
set "REMOTE_DEPLOY_SCRIPT=/usr/local/bin/egg9000-bot-deploy.sh"
set "CONFIG=Release"
set "PROJECT_REL=EGG9000.Bot\EGG9000.Bot.csproj"
set "PUBLISH_DIR=%~dp0..\..\artifacts\publish-bot"
set "WORKSPACE_ROOT=%~dp0..\..\"

if not "%~1"=="" set "CONTAINER_IP=%~1"
if not "%~2"=="" set "REMOTE_USER=%~2"

echo [1/5] Validating required tools...
where dotnet >nul 2>nul || (echo dotnet CLI not found in PATH.& exit /b 1)
where ssh >nul 2>nul || (echo ssh not found in PATH. Install OpenSSH Client.& exit /b 1)
where scp >nul 2>nul || (echo scp not found in PATH. Install OpenSSH Client.& exit /b 1)

echo [2/5] Publishing EGG9000.Bot...
dotnet publish "%WORKSPACE_ROOT%%PROJECT_REL%" -c %CONFIG% -o "%PUBLISH_DIR%"
if errorlevel 1 (
  echo Publish failed.
  pause
  exit /b 1
)

echo [3/5] Preparing remote incoming folder...
ssh %REMOTE_USER%@%CONTAINER_IP% "mkdir -p %REMOTE_INCOMING% && rm -rf %REMOTE_INCOMING%/*"
if errorlevel 1 (
  echo Failed to prepare remote directory %REMOTE_INCOMING%.
  pause
  exit /b 1
)

echo [4/5] Copying published files to %REMOTE_USER%@%CONTAINER_IP%:%REMOTE_INCOMING% ...
scp -r "%PUBLISH_DIR%\." %REMOTE_USER%@%CONTAINER_IP%:%REMOTE_INCOMING%/
if errorlevel 1 (
  echo File copy failed.
  pause
  exit /b 1
)

echo [5/5] Running remote blue/green deploy script...
ssh %REMOTE_USER%@%CONTAINER_IP% "sed -i 's/\r$//' %REMOTE_DEPLOY_SCRIPT% && chmod +x %REMOTE_DEPLOY_SCRIPT% && %REMOTE_DEPLOY_SCRIPT% %REMOTE_INCOMING%"
if errorlevel 1 (
  echo Remote deploy script failed.
  pause
  exit /b 1
)

echo.
echo Deployment completed successfully.
echo Target: %REMOTE_USER%@%CONTAINER_IP%
pause
exit /b 0
