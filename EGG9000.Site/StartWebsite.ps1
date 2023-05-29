Import-Module WebAdministration
$siteName = "EGG9000"
$serverName = "HomeControl"
#$block = {Stop-WebSite $args[0]; Start-WebSite $args[0]};  
$block = {Start-WebSite $args[0];};  
$session = New-PSSession -ComputerName $serverName
Invoke-Command -Session $session -ScriptBlock $block -ArgumentList $siteName 