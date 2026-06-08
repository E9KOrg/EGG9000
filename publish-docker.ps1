# Build EGG9000.Bot and EGG9000.Site Docker images.
#
# Default - build both images locally:
#   .\publish-docker.ps1
#
# Build only one image:
#   .\publish-docker.ps1 -Bot
#   .\publish-docker.ps1 -Site
#
# Push to Docker Hub (requires Docker Hub login):
#   .\publish-docker.ps1 -Push
#   .\publish-docker.ps1 -Bot -Push
#
# Stream to a remote host without a registry:
#   .\publish-docker.ps1 -RemoteHost 192.168.1.66 -RemoteUser david

param(
    [switch]$Bot,
    [switch]$Site,
    [switch]$Push,
    [string]$RemoteHost = "",
    [string]$RemoteUser = "root"
)

# If neither -Bot nor -Site is specified, build both.
if (-not $Bot -and -not $Site) {
    $Bot = $true
    $Site = $true
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

function Publish-Image([string]$name) {
    $tag = "${name}:${timestamp}"
    $latest = "${name}:latest"
    if ($RemoteHost -ne "") {
        Write-Host "Streaming $latest to $RemoteUser@$RemoteHost ..." -ForegroundColor Cyan
        docker save $latest | ssh "${RemoteUser}@${RemoteHost}" "docker load"
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Transfer failed: $latest" -ForegroundColor Red
            exit 1
        }
        Write-Host "Deployed: $latest -> $RemoteUser@$RemoteHost" -ForegroundColor Green
    } elseif ($Push) {
        Write-Host "Pushing $latest to Docker Hub ..." -ForegroundColor Cyan
        docker push $tag
        docker push $latest
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Push failed: $latest" -ForegroundColor Red
            exit 1
        }
        Write-Host "Pushed: $tag" -ForegroundColor Green
    }
}

if ($Bot) {
    Write-Host "Building kendrome/egg9000bot:latest ..." -ForegroundColor Cyan
    docker build -f EGG9000.Bot/Dockerfile -t "kendrome/egg9000bot:$timestamp" -t "kendrome/egg9000bot:latest" .
    if ($LASTEXITCODE -ne 0) { Write-Host "Build failed: egg9000bot" -ForegroundColor Red; exit 1 }
    Write-Host "Built: kendrome/egg9000bot:latest" -ForegroundColor Green
    Publish-Image "kendrome/egg9000bot"
}

if ($Site) {
    Write-Host "Building kendrome/egg9000site:latest ..." -ForegroundColor Cyan
    docker build -f EGG9000.Site/Dockerfile -t "kendrome/egg9000site:$timestamp" -t "kendrome/egg9000site:latest" .
    if ($LASTEXITCODE -ne 0) { Write-Host "Build failed: egg9000site" -ForegroundColor Red; exit 1 }
    Write-Host "Built: kendrome/egg9000site:latest" -ForegroundColor Green
#    Publish-Image "kendrome/egg9000site"
        Write-Host "Pushing to Docker Hub..." -ForegroundColor Green
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$imageName = "kendrome/egg9000site"
$imageTag = "${imageName}:${timestamp}"
$imageLatest = "${imageName}:latest"
    docker push $imageTag
    docker push $imageLatest
    Write-Host "Published: $imageTag" -ForegroundColor Green
}
