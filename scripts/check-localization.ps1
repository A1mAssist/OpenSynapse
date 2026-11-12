# Keep user-facing source text in the resw files. Tests are excluded because
# localization tests intentionally contain Chinese fixtures.
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Root))
$cjk = [regex]'\p{IsCJKUnifiedIdeographs}'
$failures = [Collections.Generic.List[string]]::new()

$files = @(git -C $root ls-files | Where-Object {
    ($_ -match '^src/.+\.(cs|xaml)$' -and $_ -notmatch '^src/OpenSynapse\.App/Strings/') -or
    ($_ -match '^scripts/.+\.ps1$')
})

foreach ($relative in $files) {
    $path = Join-Path $root $relative
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $path) {
        $lineNumber++
        if ($cjk.IsMatch($line)) {
            $failures.Add("${relative}:${lineNumber}: $line")
        }
    }
}

$resourceSets = @{}
foreach ($culture in 'zh-CN', 'en-US') {
    $resourcePath = Join-Path $root "src/OpenSynapse.App/Strings/$culture/Resources.resw"
    $xml = [xml](Get-Content -LiteralPath $resourcePath -Raw)
    $bases = [Collections.Generic.HashSet[string]]::new()
    foreach ($data in $xml.root.data) {
        $name = $data.name
        $split = $name.IndexOfAny([char[]]@('.', '/'))
        $bases.Add($(if ($split -gt 0) { $name.Substring(0, $split) } else { $name })) | Out-Null
    }
    $resourceSets[$culture] = $bases
}

$xamlFiles = @(git -C $root ls-files | Where-Object { $_ -match '^src/.+\.xaml$' })
foreach ($relative in $xamlFiles) {
    $text = Get-Content -LiteralPath (Join-Path $root $relative) -Raw
    foreach ($match in [regex]::Matches($text, '(?s)<[^>]*?local:Localized\.Uid="([^"]+)"')) {
        $uid = $match.Groups[1].Value
        foreach ($culture in 'zh-CN', 'en-US') {
            $keys = $resourceSets[$culture]
            if (-not $keys.Contains($uid)) {
                $failures.Add("${relative}: Localized.Uid '$uid' is missing from $culture")
            }
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { $_ }
    exit 1
}

'Localization check: passed'
exit 0
