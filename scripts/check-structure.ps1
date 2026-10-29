# 结构体检。默认只报告并按预算判定退出码；-UpdateBudget 会重写 .structure-budget.json。
param(
    [switch]$UpdateBudget,
    [int]$NewFileLineLimit = 600,
    [int]$NewFilePublicMemberLimit = 100
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
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
    "已写入 $budgetPath（$($budget.files.Count) 个文件条目）"
    exit 0
}

if (-not (Test-Path $budgetPath)) {
    throw '缺少 .structure-budget.json，先跑一次 -UpdateBudget'
}

$saved = Get-Content $budgetPath -Raw | ConvertFrom-Json
$failures = @()
foreach ($entry in $saved.files) {
    $actual = $files | Where-Object { $_.Path -eq $entry.path }
    if (-not $actual) { $failures += "预算条目已失效：$($entry.path)"; continue }
    if ($actual.Lines -gt $entry.maxLines) { $failures += "$($entry.path) 超预算：$($actual.Lines) > $($entry.maxLines)" }
    if ($entry.maxPublicMembers -and $actual.PublicMembers -gt $entry.maxPublicMembers) {
        $failures += "$($entry.path) public 超预算：$($actual.PublicMembers) > $($entry.maxPublicMembers)"
    }
}

"超 $NewFileLineLimit 行的文件：$($over.Count)"
$over | Select-Object -First 12 | ForEach-Object { '{0,6}  {1,4} public  {2}' -f $_.Lines, $_.PublicMembers, $_.Path }

if ($failures.Count -gt 0) {
    ''
    '预算失败：'
    $failures | ForEach-Object { "  $_" }
    exit 1
}

'结构预算：通过'
exit 0
