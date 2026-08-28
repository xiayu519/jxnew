$ErrorActionPreference = 'Stop'

$cleanRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $cleanRoot 'UnityProject'
$resourcePaths = @(
    'Assets\Mods\XinJianXia\Content',
    'Assets\Shared\JxShared_XinJianXiaBase\Content',
    'Assets\Shared\JxShared_DaoJian543Base\Content',
    'Assets\Mods\LengJianHanMei\Content',
    'Assets\Mods\MengLiHuiMou\Content'
)
$resultPath = Join-Path $projectPath 'Library\JxNewCleanSetup\setup.result'
$requestPath = Join-Path $projectPath 'Library\JxNewCleanSetup\setup.request'
$progressPath = Join-Path $projectPath 'Library\JxNewCleanSetup\setup.progress'
$logPath = Join-Path $projectPath 'Logs\JxNewCleanSetup.log'
$unityPath = $env:UNITY_EDITOR

foreach ($relativePath in $resourcePaths) {
    $resourcePath = Join-Path $projectPath $relativePath
    if (-not (Test-Path -LiteralPath $resourcePath -PathType Container)) {
        throw "Import JxNewResources-20260828.unitypackage before setup. Missing: $relativePath"
    }
}

if ([string]::IsNullOrWhiteSpace($unityPath)) {
    $unityPath = 'C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe'
}
if (-not (Test-Path -LiteralPath $unityPath -PathType Leaf)) {
    throw 'Unity 6000.5.4f1 was not found. Set UNITY_EDITOR to Unity.exe.'
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $resultPath)) | Out-Null
[System.IO.Directory]::CreateDirectory((Split-Path -Parent $logPath)) | Out-Null
foreach ($path in @($resultPath, $progressPath)) {
    if ([System.IO.File]::Exists($path)) {
        [System.IO.File]::Delete($path)
    }
}

$lockPath = Join-Path $projectPath 'Temp\UnityLockfile'
if (Test-Path -LiteralPath $lockPath) {
    Write-Host '[INFO] Unity Editor is open. Configuring Editor Simulate Mode...'
    [System.IO.File]::WriteAllText($requestPath, 'setup')
    $deadline = (Get-Date).AddMinutes(20)
    $lastProgress = ''
    while ((Get-Date) -lt $deadline -and
           -not [System.IO.File]::Exists($resultPath)) {
        if ([System.IO.File]::Exists($progressPath)) {
            $progress = [System.IO.File]::ReadAllText($progressPath).Trim()
            if ($progress -ne $lastProgress) {
                Write-Host "[INFO] $progress"
                $lastProgress = $progress
            }
        }
        Start-Sleep -Seconds 1
    }
    if (-not [System.IO.File]::Exists($resultPath)) {
        throw 'Timed out waiting for the Unity Editor setup request.'
    }
}
else {
    Write-Host '[INFO] Starting Unity 6000.5.4f1 to configure Editor Simulate Mode...'
    $arguments = @(
        '-batchmode',
        '-projectPath', ('"' + $projectPath + '"'),
        '-executeMethod',
        'JxNewMod.Editor.CleanSetup.JxNewModCleanProjectSetup.ConfigureFromCommandLine',
        '-quit',
        '-logFile', ('"' + $logPath + '"')
    )
    $startParameters = @{
        FilePath = $unityPath
        ArgumentList = $arguments
        WindowStyle = 'Hidden'
        Wait = $true
        PassThru = $true
    }
    $process = Start-Process @startParameters
    if ($process.ExitCode -ne 0 -and
        -not [System.IO.File]::Exists($resultPath)) {
        throw "Unity setup failed with exit code $($process.ExitCode). See $logPath"
    }
}

if (-not [System.IO.File]::Exists($resultPath)) {
    throw "Unity did not create a setup result. See $logPath"
}

$result = [System.IO.File]::ReadAllText($resultPath)
if (-not $result.StartsWith('SUCCESS', [System.StringComparison]::Ordinal)) {
    throw "Resource setup failed:`n$result"
}

Write-Host '[SUCCESS] All Mod simulation manifests are ready. No bundles were built.'
Write-Host 'Open Assets/Scenes/main.unity and press Play.'
Write-Host $result
