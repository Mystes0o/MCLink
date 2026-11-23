$ErrorActionPreference = 'Stop'
$zip = Join-Path ([IO.Path]::GetTempPath()) ("mclink-"+[guid]::NewGuid()+".zip")
try {
    Invoke-WebRequest -UseBasicParsing 'https://github.com/EasyTier/EasyTier/releases/download/v2.6.4/easytier-windows-x86_64-v2.6.4.zip' -OutFile $zip
    if ((Get-FileHash $zip).Hash -ne '27af91e270e554709b048bd32327fefd2dfce5062ae1e8701af7550c6f525f84') { throw 'EasyTier archive checksum mismatch' }
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $PSScriptRoot 'runtime') -Force
} finally { if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip } }
