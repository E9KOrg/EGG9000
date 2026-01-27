# Build and push EGG9000.Site to Docker Hub
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$imageName = "kendrome/egg9000site"
$imageTag = "${imageName}:${timestamp}"
$imageLatest = "${imageName}:latest"

Write-Host "Building image: $imageTag" -ForegroundColor Green

# Build from solution root
docker build -f EGG9000.Site/Dockerfile -t $imageTag -t $imageLatest .

if ($LASTEXITCODE -eq 0) {
    Write-Host "Pushing to Docker Hub..." -ForegroundColor Green
    docker push $imageTag
    docker push $imageLatest
    Write-Host "Published: $imageTag" -ForegroundColor Green
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}