$ErrorActionPreference = 'Stop'
$duoRoot = $PSScriptRoot
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$duoLib = Join-Path $duoRoot 'lib'
New-Item -ItemType Directory -Force -Path $duoLib | Out-Null
$duoZip = Join-Path $duoLib 'NAudio.zip'
if (!(Test-Path -LiteralPath $duoZip)) {
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/naudio/1.10.0/naudio.1.10.0.nupkg' -OutFile $duoZip
}
$duoHash = (Get-FileHash -LiteralPath $duoZip -Algorithm SHA256).Hash
if ($duoHash -ne 'E4A80EED41DAD794695F9C114432DE4667D20BDFA80825AA94D159708CF29217') { throw 'El archivo de NAudio no coincide con la versión verificada.' }
Expand-Archive -LiteralPath $duoZip -DestinationPath (Join-Path $duoLib 'package') -Force
Copy-Item -LiteralPath (Join-Path $duoLib 'package\lib\net35\NAudio.dll') -Destination (Join-Path $duoRoot 'NAudio.dll')
