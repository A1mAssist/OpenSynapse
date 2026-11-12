# Structure check. Reports by default and fails on budget violations; -UpdateBudget rewrites .structure-budget.json.
param(
    [switch]$UpdateBudget,
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [int]$NewFileLineLimit = 600,
    [int]$NewFilePublicMemberLimit = 100
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Root))
$budgetPath = Join-Path $root '.structure-budget.json'

$files = Get-ChildItem (Join-Path $root 'src') -Recurse -Include *.cs, *.xaml |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\artifacts\\' } |
    ForEach-Object {
        $lines = Get-Content $_.FullName
        [pscustomobject]@{
            Path          = $_.FullName.Replace("$root\", '').Replace('\', '/')
            Lines         = $lines.Count
            PublicMembers = @($lines | Where-Object { $_ -match '^    public\s' }).Count
        }
    }

$over = $files | Where-Object { $_.Lines -gt $NewFileLineLimit } | Sort-Object Lines -Descending

if ($UpdateBudget) {
    $budget = [ordered]@{
        newFileLineLimit        = $NewFileLineLimit
        newFilePublicMemberLimit = $NewFilePublicMemberLimit
        files                   = @($over | ForEach-Object {
            if ($_.PublicMembers -gt $NewFilePublicMemberLimit) {
                [ordered]@{ path = $_.Path; maxLines = $_.Lines; maxPublicMembers = $_.PublicMembers }
            }
            else {
                [ordered]@{ path = $_.Path; maxLines = $_.Lines }
            }
        })
        groups                  = @(
            [ordered]@{
                pattern          = 'src/OpenSynapse.App/ViewModels/MainViewModel*.cs'
                maxLines         = [int]($files | Where-Object { $_.Path -like 'src/OpenSynapse.App/ViewModels/MainViewModel*.cs' } | Measure-Object Lines -Sum).Sum
                maxPublicMembers = [int]($files | Where-Object { $_.Path -like 'src/OpenSynapse.App/ViewModels/MainViewModel*.cs' } | Measure-Object PublicMembers -Sum).Sum
            }
        )
    }
    $budget | ConvertTo-Json -Depth 5 | Set-Content -Path $budgetPath -Encoding utf8
    "Wrote $budgetPath ($($budget.files.Count) file entries)"
    exit 0
}

if (-not (Test-Path $budgetPath)) {
    throw 'Missing .structure-budget.json. Run -UpdateBudget once first.'
}

$saved = Get-Content $budgetPath -Raw | ConvertFrom-Json
if (-not $PSBoundParameters.ContainsKey('NewFileLineLimit')) { $NewFileLineLimit = $saved.newFileLineLimit }
if (-not $PSBoundParameters.ContainsKey('NewFilePublicMemberLimit')) { $NewFilePublicMemberLimit = $saved.newFilePublicMemberLimit }
$failures = @()
foreach ($entry in $saved.files) {
    $actual = $files | Where-Object { $_.Path -ceq $entry.path }
    if (-not $actual) { $failures += "Stale budget entry: $($entry.path)"; continue }
    if ($actual.Lines -gt $entry.maxLines) { $failures += "$($entry.path) over budget: $($actual.Lines) > $($entry.maxLines)" }
}
foreach ($file in $files) {
    $entry = $saved.files | Where-Object { $_.path -ceq $file.Path }
    $lineLimit = if ($entry) { $entry.maxLines } else { $NewFileLineLimit }
    $publicLimit = if ($entry -and $null -ne $entry.maxPublicMembers) { $entry.maxPublicMembers } else { $NewFilePublicMemberLimit }
    if (-not $entry -and $file.Lines -gt $lineLimit) {
        $failures += "$($file.Path) new file over budget: $($file.Lines) > $lineLimit"
    }
    if ($file.Path.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) -and $file.PublicMembers -gt $publicLimit) {
        $failures += "$($file.Path) public members over budget: $($file.PublicMembers) > $publicLimit"
    }
}
foreach ($group in $saved.groups) {
    $matched = @($files | Where-Object { $_.Path -clike $group.pattern })
    if ($matched.Count -eq 0) { $failures += "Stale group budget: $($group.pattern)"; continue }
    $lines = [int]($matched | Measure-Object Lines -Sum).Sum
    $members = [int]($matched | Measure-Object PublicMembers -Sum).Sum
    if ($null -ne $group.maxLines -and $lines -gt $group.maxLines) {
        $failures += "$($group.pattern) group lines over budget: $lines > $($group.maxLines)"
    }
    if ($null -ne $group.maxPublicMembers -and $members -gt $group.maxPublicMembers) {
        $failures += "$($group.pattern) group public members over budget: $members > $($group.maxPublicMembers)"
    }
}

"Files over $NewFileLineLimit lines: $($over.Count)"
$over | Select-Object -First 12 | ForEach-Object { '{0,6}  {1,4} public  {2}' -f $_.Lines, $_.PublicMembers, $_.Path }

if ($failures.Count -gt 0) {
    ''
    'Budget failures:'
    $failures | ForEach-Object { "  $_" }
    exit 1
}

'Structure budget: passed'
exit 0
