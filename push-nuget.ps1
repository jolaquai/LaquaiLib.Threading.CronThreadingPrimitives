param(
    [Parameter(Mandatory)]
    [string]$ApiKey
)

$packages = Get-ChildItem -Recurse -Include *.nupkg, *.snupkg -File |
    Where-Object { $_.FullName -notmatch '\\obj\\' }

if (-not $packages) {
    Write-Error "No packages found. Run 'dotnet build -c Release' first."
    exit 1
}

foreach ($pkg in $packages) {
    dotnet nuget push $pkg.FullName --api-key $ApiKey --source https://api.nuget.org/v3/index.json --skip-duplicate
}
