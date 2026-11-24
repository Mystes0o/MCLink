param(
    [string]$SdkRoot = $env:ANDROID_HOME,
    [string]$JavaHome = $env:JAVA_HOME,
    [string]$EclipseCompiler,
    [string]$RelayFile,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $project 'runtime/android-tools'
if (!$RelayFile) { $RelayFile = Join-Path $project 'relay.txt' }
$RelayFile = (Resolve-Path -LiteralPath $RelayFile).Path
if (!$SdkRoot) { $SdkRoot = Join-Path $tools 'sdk' }
if (!$OutputPath) { $OutputPath = Join-Path $project 'MCLinkForAndroid.apk' }
if (!$JavaHome) {
    $javaCommand = Get-Command java.exe -ErrorAction Stop
    $JavaHome = Split-Path -Parent (Split-Path -Parent $javaCommand.Source)
}
if (!$EclipseCompiler) {
    $localCompiler = Join-Path $tools 'ecj.jar'
    if (Test-Path -LiteralPath $localCompiler) { $EclipseCompiler = $localCompiler }
}

function Invoke-Tool([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Build tool failed ($LASTEXITCODE) - $Program" }
}

$buildTools = Join-Path $SdkRoot 'build-tools/35.0.0'
$platform = Join-Path $SdkRoot 'platforms/android-35/android.jar'
$java = Join-Path $JavaHome 'bin/java.exe'
$javac = Join-Path $JavaHome 'bin/javac.exe'
$keytool = Join-Path $JavaHome 'bin/keytool.exe'
$jni = Join-Path $tools 'jni/arm64-v8a/libeasytier_android_jni.so'
$work = Join-Path $tools ('build-' + [Guid]::NewGuid().ToString('N'))
$keys = Join-Path $tools 'debug.keystore'
$classes = Join-Path $work 'classes'
$manifest = Join-Path $PSScriptRoot 'src/main/AndroidManifest.xml'

if (!(Test-Path -LiteralPath $platform)) { throw 'Install Android SDK platform 35 and build-tools 35.0.0.' }
if (!(Test-Path -LiteralPath $jni)) { throw 'Run setup.ps1 to download the required EasyTier Android library.' }
if ((Get-FileHash -LiteralPath $jni).Hash -ne 'FC0AED1D095D53434CB77E6EA239CCEE9928C1C0EB37917B2D56F6E8D5404DCF') {
    throw 'EasyTier Android library checksum failed. Run setup.ps1 to fetch the pinned library.'
}

New-Item -ItemType Directory -Path $classes -Force | Out-Null
$oldJavaHome = $env:JAVA_HOME
try {
    $env:JAVA_HOME = $JavaHome
    $sources = @(Get-ChildItem (Join-Path $PSScriptRoot 'src/main/java') -Recurse -Filter '*.java' |
        ForEach-Object FullName)
    $compileArgs = @('-encoding','UTF-8','-classpath',$platform,'-d',$classes) + $sources
    if (Test-Path -LiteralPath $javac) { Invoke-Tool $javac (@('--release','8') + $compileArgs) }
    elseif ($EclipseCompiler) { Invoke-Tool $java (@('-jar',$EclipseCompiler,'-8','-nowarn') + $compileArgs) }
    else { throw 'Set JavaHome to a JDK or install the Eclipse compiler via setup.ps1.' }
    $dex = @(Get-ChildItem $classes -Recurse -Filter '*.class' | ForEach-Object FullName)
    Invoke-Tool (Join-Path $buildTools 'd8.bat') (@('--lib',$platform,'--min-api','26','--output',$work) + $dex)

    $unsigned = Join-Path $work 'unsigned.apk'
    Invoke-Tool (Join-Path $buildTools 'aapt2.exe') @('link','-o',$unsigned,'-I',$platform,
        '--manifest',$manifest,'--version-code','1','--version-name','0.1.0')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($unsigned,[IO.Compression.ZipArchiveMode]::Update)
    try {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $work 'classes.dex'),
            'classes.dex',[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$jni,
            'lib/arm64-v8a/libeasytier_android_jni.so',[IO.Compression.CompressionLevel]::NoCompression) | Out-Null
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$RelayFile,
            'assets/relay.txt',[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        foreach ($asset in (Get-ChildItem (Join-Path $PSScriptRoot 'src/main/assets') -File)) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$asset.FullName,
                ('assets/' + $asset.Name),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }

    $aligned = Join-Path $work 'aligned.apk'
    Invoke-Tool (Join-Path $buildTools 'zipalign.exe') @('-P','16','-f','4',$unsigned,$aligned)
    if (!(Test-Path -LiteralPath $keys)) {
        Invoke-Tool $keytool @('-genkeypair','-keystore',$keys,'-alias','androiddebugkey',
            '-storepass','android','-keypass','android','-dname','CN=Android Debug,O=MCLink',
            '-keyalg','RSA','-keysize','2048','-validity','10000')
    }
    Invoke-Tool (Join-Path $buildTools 'apksigner.bat') @('sign','--ks',$keys,'--ks-key-alias','androiddebugkey',
        '--ks-pass','pass:android','--key-pass','pass:android','--out',$OutputPath,$aligned)
    Invoke-Tool (Join-Path $buildTools 'apksigner.bat') @('verify','--verbose',$OutputPath)
    Get-Item -LiteralPath $OutputPath
} finally {
    $env:JAVA_HOME = $oldJavaHome
    $resolved = (Resolve-Path -LiteralPath $work).Path
    $expected = (Resolve-Path -LiteralPath $tools).Path + [IO.Path]::DirectorySeparatorChar + 'build-'
    if ($resolved.StartsWith($expected,[StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -Recurse -Force -LiteralPath $resolved
    }
}
