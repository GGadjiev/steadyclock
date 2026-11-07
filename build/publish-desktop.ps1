param(
    [string]$Configuration = 'Release',
    [switch]$SkipArchives
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\SteadyClock.Desktop\SteadyClock.Desktop.csproj'
$publishRoot = Join-Path $root 'publish'
$rids = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')

foreach ($rid in $rids) {
    $output = Join-Path $publishRoot "SteadyClock-$rid"
    if (Test-Path $output) {
        Remove-Item $output -Recurse -Force
    }

    Write-Host "Publishing $rid..."
    dotnet publish $project `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $output
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed for $rid."
    }

    $readme = if ($rid -like 'win-*') {
        @(
            'SteadyClock',
            '',
            'Запуск: SteadyClock.exe',
            'Права администратора не требуются.',
            'Данные: %LOCALAPPDATA%\MyTimeTracker'
        )
    }
    else {
        @(
            'SteadyClock',
            '',
            'Запуск:',
            '  chmod +x SteadyClock',
            '  ./SteadyClock',
            '',
            'Zip может не сохранить Unix-права на исполнение,'
            'поэтому chmod нужен после распаковки.',
            'Linux data: $XDG_DATA_HOME/steadyclock или ~/.local/share/steadyclock',
            'macOS data: ~/Library/Application Support/SteadyClock'
        )
    }

    [IO.File]::WriteAllLines(
        (Join-Path $output 'README.txt'),
        $readme,
        (New-Object System.Text.UTF8Encoding($false)))

    if (-not $SkipArchives) {
        $archive = Join-Path $publishRoot "SteadyClock-$rid.zip"
        if (Test-Path $archive) {
            Remove-Item $archive -Force
        }

        Write-Host "Archiving $rid..."
        Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -Force
    }
}

Write-Host 'Done.'
