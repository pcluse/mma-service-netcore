
$projectFolder=$($PSScriptRoot)
# $outputFolder="C:\Program Files\PLS\MMA\Service"
$outputFolder="C:\Packages\Service"

$onlyPublish = $false

$serviceName = "MMAService"
$serviceExe = "MMAService.exe"

If (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping service $($serviceName) if running"
    Stop-Service -Name $serviceName -ErrorAction SilentlyContinue -Force
    If (Stop-Process -Name $serviceName -ErrorAction SilentlyContinue) {
        Write-Host "Stop Process $($serviceName)"
        Stop-Process -Name $serviceName -Force
    }
    Write-Host "Delete service $($serviceName)"
    Start-Process -FilePath "C:\Windows\System32\sc.exe" -ArgumentList "delete ""$($serviceName)""" -Wait -NoNewWindow
}

# Publish the service (with files to run it)
# @TODO exclude runtime and test with installed dotnet core 2.1 runtime
Write-Host "Publish service $($serviceName)"
Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' `
    -ArgumentList "publish --configuration Release --runtime win10-x64 --output ""$outputFolder""" `
    -WorkingDirectory $projectFolder -Wait -NoNewWindow

# https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-2.1

if (-not $onlyPublish) {
    Write-Host "Create service $($serviceName)"
    Start-Process -FilePath "sc" -ArgumentList "create ""$($serviceName)"" binPath= ""$($outputFolder)\$($serviceExe)"" start=auto depend=ProfSvc/EventLog" -Wait -NoNewWindow

    Write-Host "Start service $($serviceName)"
    Start-Service -Name $serviceName
}