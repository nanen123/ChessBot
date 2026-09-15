param([string]$UnityData = 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$validationOut = Join-Path $project 'output/local-chess-validation'
New-Item -ItemType Directory -Force -Path $validationOut | Out-Null
$compiler = Join-Path $UnityData 'DotNetSdkRoslyn/csc.dll'
$dotnet = Join-Path $UnityData 'NetCoreRuntime/dotnet.exe'
$mono = Join-Path $UnityData 'MonoBleedingEdge/bin/mono.exe'
$framework = Join-Path $UnityData 'MonoBleedingEdge/lib/mono/4.8-api'
$baseRefs = @('mscorlib.dll','System.dll','System.Core.dll') | ForEach-Object { Join-Path $framework $_ }
$baseRefs += Join-Path $framework 'Facades/netstandard.dll'
function Compile-Chess([string]$Name, [string[]]$Sources, [string[]]$References, [string]$Target = 'library') {
    $extension = if ($Target -eq 'exe') { '.exe' } else { '.dll' }
    $argsFile = Join-Path $validationOut ($Name + '.rsp')
    $lines = @('/nologo','/langversion:9.0','/nostdlib+',"/target:$Target",('/out:"' + (Join-Path $validationOut ($Name + $extension)) + '"'))
    $lines += ($baseRefs + $References | Select-Object -Unique | ForEach-Object { '/r:"' + $_ + '"' })
    $lines += ($Sources | ForEach-Object { '"' + $_ + '"' })
    [IO.File]::WriteAllLines($argsFile, $lines)
    & $dotnet $compiler ('@' + $argsFile)
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $Name" }
}
$core = Join-Path $validationOut 'ChessBot.Chess.Core.dll'
$application = Join-Path $validationOut 'ChessBot.Chess.Application.dll'
$presentation = Join-Path $validationOut 'ChessBot.Chess.Presentation.dll'
Compile-Chess 'ChessBot.Chess.Core' (Get-ChildItem "$project/Assets/Scripts/Chess/Core/*.cs").FullName @()
Compile-Chess 'ChessBot.Chess.Application' (Get-ChildItem "$project/Assets/Scripts/Chess/Application/*.cs").FullName @($core)
$unityRefs = (Get-ChildItem "$UnityData/Managed/UnityEngine/*.dll").FullName
$unityRefs += "$project/Library/ScriptAssemblies/UnityEngine.UI.dll"
$unityRefs += "$project/Library/ScriptAssemblies/Unity.InputSystem.dll"
Compile-Chess 'ChessBot.Chess.Presentation' (Get-ChildItem "$project/Assets/Scripts/Chess/Presentation/*.cs").FullName ($unityRefs + @($core, $application))
Compile-Chess 'ChessBot.Bootstrap' (Get-ChildItem "$project/Assets/Scripts/Bootstrap/*.cs").FullName ($unityRefs + @($core, $application, $presentation))
$editorRefs = (Get-ChildItem "$UnityData/Managed/UnityEditor*.dll" | Where-Object Name -ne 'UnityEditor.dll').FullName
Compile-Chess 'ChessBot.Bootstrap.Editor' (Get-ChildItem "$project/Assets/Scripts/Bootstrap/Editor/*.cs").FullName ($unityRefs + $editorRefs + @($core, $application, $presentation))
$nunit = (Get-ChildItem "$project/Library/PackageCache" -Filter nunit.framework.dll -Recurse | Select-Object -First 1).FullName
if (!$nunit) { throw 'NUnit package is unavailable.' }
Compile-Chess 'ChessBot.Chess.Tests' (Get-ChildItem "$project/Assets/Tests/EditMode/*.cs").FullName (@($core, $application, $presentation, $nunit) + $unityRefs)
Compile-Chess 'RunChessTests' @("$PSScriptRoot/RunChessTests.cs") @($nunit) 'exe'
Copy-Item -LiteralPath $nunit -Destination $validationOut -Force
& $mono (Join-Path $validationOut 'RunChessTests.exe') (Join-Path $validationOut 'ChessBot.Chess.Tests.dll')
if ($LASTEXITCODE -ne 0) { throw 'Chess tests failed.' }
