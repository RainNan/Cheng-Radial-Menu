$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $chengCache = Join-Path ([IO.Path]::GetTempPath()) 'ChengRadialMenuBuild'
    $chengObj = (Join-Path $chengCache 'obj') + [IO.Path]::DirectorySeparatorChar
    $chengBin = (Join-Path $chengCache 'bin') + [IO.Path]::DirectorySeparatorChar
    dotnet publish src/ChengOrbit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true "-p:BaseIntermediateOutputPath=$chengObj" "-p:BaseOutputPath=$chengBin" -o dist
    if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
    Write-Host '已生成 dist\RadialMenu.exe，双击即可运行。'
} finally { Pop-Location }