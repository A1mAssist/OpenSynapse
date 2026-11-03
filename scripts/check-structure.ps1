# 结构体检。默认只报告并按预算判定退出码；-UpdateBudget 会重写 .structure-budget.json。
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
    "已写入 $budgetPath（$($budget.files.Count) 个文件条目）"
    exit 0
}

if (-not (Test-Path $budgetPath)) {
    throw '缺少 .structure-budget.json，先跑一次 -UpdateBudget'
}

$saved = Get-Content $budgetPath -Raw | ConvertFrom-Json
if (-not $PSBoundParameters.ContainsKey('NewFileLineLimit')) { $NewFileLineLimit = $saved.newFileLineLimit }
if (-not $PSBoundParameters.ContainsKey('NewFilePublicMemberLimit')) { $NewFilePublicMemberLimit = $saved.newFilePublicMemberLimit }
$failures = @()
foreach ($entry in $saved.files) {
    $actual = $files | Where-Object { $_.Path -ceq $entry.path }
    if (-not $actual) { $failures += "预算条目已失效：$($entry.path)"; continue }
    if ($actual.Lines -gt $entry.maxLines) { $failures += "$($entry.path) 超预算：$($actual.Lines) > $($entry.maxLines)" }
}
foreach ($file in $files) {
    $entry = $saved.files | Where-Object { $_.path -ceq $file.Path }
    $lineLimit = if ($entry) { $entry.maxLines } else { $NewFileLineLimit }
    $publicLimit = if ($entry -and $null -ne $entry.maxPublicMembers) { $entry.maxPublicMembers } else { $NewFilePublicMemberLimit }
    if (-not $entry -and $file.Lines -gt $lineLimit) {
        $failures += "$($file.Path) 新文件超预算：$($file.Lines) > $lineLimit"
    }
    if ($file.Path.EndsWith('.cs', [StringComparison]::OrdinalIgnoreCase) -and $file.PublicMembers -gt $publicLimit) {
        $failures += "$($file.Path) public 超预算：$($file.PublicMembers) > $publicLimit"
    }
}
foreach ($group in $saved.groups) {
    $matched = @($files | Where-Object { $_.Path -clike $group.pattern })
    if ($matched.Count -eq 0) { $failures += "分组预算已失效：$($group.pattern)"; continue }
    $lines = [int]($matched | Measure-Object Lines -Sum).Sum
    $members = [int]($matched | Measure-Object PublicMembers -Sum).Sum
    if ($null -ne $group.maxLines -and $lines -gt $group.maxLines) {
        $failures += "$($group.pattern) 分组行数超预算：$lines > $($group.maxLines)"
    }
    if ($null -ne $group.maxPublicMembers -and $members -gt $group.maxPublicMembers) {
        $failures += "$($group.pattern) 分组 public 超预算：$members > $($group.maxPublicMembers)"
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
