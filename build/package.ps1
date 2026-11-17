param([string]$Output = 'artifacts')
$ErrorActionPreference = 'Stop'
$bundle = Join-Path $Output 'ACadSmartLeader.bundle'
New-Item -ItemType Directory -Force $bundle | Out-Null
Copy-Item installer/ACadSmartLeader.bundle/PackageContents.xml $bundle
foreach ($target in @(@('net48','AutoCAD2021-2024'), @('net8.0-windows','AutoCAD2025'))) {
    $destination = Join-Path $bundle ('Contents/' + $target[1])
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item ('src/ACadSmartLeader/bin/Release/' + $target[0] + '/ACadSmartLeader.dll') $destination
    Copy-Item examples/label-rules.json, examples/demo-schema.json $destination
    Copy-Item (Join-Path $Output 'ACadSmartLeader.cuix') $destination
}
Compress-Archive -Path $bundle -DestinationPath (Join-Path $Output 'ACadSmartLeader-0.1.0.zip') -Force
