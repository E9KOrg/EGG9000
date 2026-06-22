# Deploy secrets to remote Docker host via SSH (Linux compatible)

param(
    [Parameter(Mandatory=$true)]
    [string]$RemoteHost,
    
    [Parameter(Mandatory=$true)]
    [string]$RemoteUser,
    
    [string]$SecretsJsonPath = "$env:APPDATA\Microsoft\UserSecrets\DEV9001\secrets.json"
)

# Read secrets from your local secrets.json
if (-not (Test-Path $SecretsJsonPath)) {
    Write-Error "Secrets file not found at: $SecretsJsonPath"
    exit 1
}

$secrets = Get-Content $SecretsJsonPath | ConvertFrom-Json

# Extract values
$dbConn = $secrets.ConnectionStrings.DefaultConnection
$discordClientId = $secrets.ConnectionStrings.ClientId
$discordToken = $secrets.ConnectionStrings.Token
$discordSecret = $secrets.ConnectionStrings.ClientSecret
$bugsnagKey = $secrets.ConnectionStrings.BugSnagApiKey
$rabbitmqConn = $secrets.ConnectionStrings.RabbitMQServer
$apiSalt = $secrets.ConnectionStrings.ApiSalt


# Function to create secret on remote Linux host
function New-RemoteDockerSecret {
    param(
        [string]$Name,
        [string]$Value,
        [string]$RemoteHost,
        [string]$RemoteUser
    )
    
    # Base64 encode the value to safely transfer special characters
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $base64 = [Convert]::ToBase64String($bytes)
    
    # Remove old secret if exists, then create new one using base64 decode
    $cmd = @"
docker secret rm $Name 2>/dev/null || true
echo '$base64' | base64 -d | docker secret create $Name -
"@
    
    # Execute via SSH
    $result = ssh "$RemoteUser@$RemoteHost" $cmd 2>&1
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Created secret: $Name" -ForegroundColor Green
    } else {
        Write-Host "✗ Failed to create secret: $Name" -ForegroundColor Red
        Write-Host "  Error: $result" -ForegroundColor Yellow
    }
}

Write-Host "Deploying Docker secrets to $RemoteHost..." -ForegroundColor Cyan
Write-Host "Remote user: $RemoteUser" -ForegroundColor Gray

# Test SSH connectivity first
Write-Host "`nTesting SSH connection..." -ForegroundColor Yellow
$sshTest = ssh -o ConnectTimeout=5 "$RemoteUser@$RemoteHost" "echo 'Connected'" 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to connect to remote host. Please check SSH keys and connectivity."
    Write-Host "Error: $sshTest" -ForegroundColor Red
    exit 1
}

Write-Host "✓ SSH connection successful`n" -ForegroundColor Green

# Verify Docker is available on remote
Write-Host "Verifying Docker on remote..." -ForegroundColor Yellow
$dockerTest = ssh "$RemoteUser@$RemoteHost" "docker info > /dev/null 2>&1 && echo 'OK'" 2>&1

if ($dockerTest -ne "OK") {
    Write-Error "Docker is not available or you don't have permission on the remote host."
    exit 1
}

Write-Host "✓ Docker is available`n" -ForegroundColor Green

# Create all secrets
Write-Host "Creating secrets..." -ForegroundColor Cyan
New-RemoteDockerSecret "db_connection_string" $dbConn $RemoteHost $RemoteUser
New-RemoteDockerSecret "discord_client_id" $discordClientId $RemoteHost $RemoteUser
New-RemoteDockerSecret "discord_token" $discordToken $RemoteHost $RemoteUser
New-RemoteDockerSecret "discord_client_secret" $discordSecret $RemoteHost $RemoteUser
New-RemoteDockerSecret "bugsnag_api_key" $bugsnagKey $RemoteHost $RemoteUser
New-RemoteDockerSecret "rabbitmq_connection" $rabbitmqConn $RemoteHost $RemoteUser
if (-not [string]::IsNullOrEmpty($apiSalt)) {
    New-RemoteDockerSecret "egg_inc_api_salt" $apiSalt $RemoteHost $RemoteUser
} else {
    Write-Host "! Skipping egg_inc_api_salt (ConnectionStrings.ApiSalt not set in secrets.json)" -ForegroundColor Yellow
}


Write-Host "`n✓ All secrets deployed!" -ForegroundColor Green

# Verify secrets were created
Write-Host "`nVerifying secrets on remote:" -ForegroundColor Yellow
ssh "$RemoteUser@$RemoteHost" "docker secret ls"

Write-Host "`nDeployment complete! You can now deploy your stack." -ForegroundColor Cyan