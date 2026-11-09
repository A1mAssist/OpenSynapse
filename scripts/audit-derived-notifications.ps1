# Heuristic audit for derived-property change notifications.
#
# For every expression-bodied Visibility/bool/string/basic-type property, walk the
# fields/properties it reads, find the methods that assign those members, and report
# whether each assignment path notifies the derived property (by name, by an empty
# PropertyChanged, via NotifyStateChanged(), or through a notifying helper).
#
# It is a review aid, not a compiler: cross-object state, generated code and
# reflection are out of scope. Rows with Notified=no are the candidates to inspect.
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string]$Output = '',
    [switch]$FailOnMissing
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Root))
$viewModels = Join-Path $root 'src/OpenSynapse.App/ViewModels'
$outputPath = if ($Output) { $Output } else { Join-Path $root 'artifacts/derived-notifications.csv' }

$files = Get-ChildItem -LiteralPath $viewModels -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
    Sort-Object FullName

$excluded = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
@(
    'Visibility','Visible','Collapsed','nameof','AppStrings','Localized','Text','Texts','FormatText',
    'Math','Enum','Convert','Array','Enumerable','Task','List','Dictionary','HashSet',
    'ObservableCollection','IReadOnlyList','IReadOnlyCollection','StringComparison','CultureInfo',
    'DateTime','TimeSpan','Color','Colors','Size','Point','Rect','Thickness','Brush',
    'Contains','Length','Any','All','First','FirstOrDefault','ToString','Count','Value',
    'HasValue','this','base','return','var','out','ref','in','is','as','not','and','or',
    'null','true','false','new','typeof','default','when','with','switch','case','break','continue',
    'public','private','internal','protected','static','readonly','partial','sealed',
    'get','set','init','Equals','ReferenceEquals','SequenceEqual','StartsWith','EndsWith',
    'ToArray','ToList','VisibleWhen','CollapsedWhen','String','Boolean','Int32','Double','_'
) | ForEach-Object { [void]$excluded.Add($_) }

$propertyPattern = '^\s*(?<access>public|internal|protected|private)\s+(?:(?:static|sealed|override|virtual|new|required|readonly|unsafe)\s+)*(?<type>Visibility|bool|string|int|double|float|long|ushort|byte)\s+(?<name>[A-Za-z_]\w*)\s*=>\s*(?<expr>.*)$'
$methodPattern = '^\s*(?<access>public|private|internal|protected)\s+(?:(?:static|sealed|async|override|virtual|new|partial|unsafe|readonly|extern)\s+)*(?:(?<type>[A-Za-z_][\w<>\[\],\.\?]*)\s+)?(?<name>(?!(?:static|sealed|async|override|virtual|new|partial|unsafe|readonly|extern|const)\b)[A-Za-z_]\w*)(?:<[^>]{1,40}>)?\s*\('
$classPattern = '^(?:(?:public|internal|private|protected|static|sealed|partial|abstract|readonly|file|unsafe)\s+)*(?:class|record|struct|interface)\s+(?<name>[A-Za-z_]\w*)'

# Only properties reachable from XAML bindings matter for stale-UI bugs.
$boundNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
Get-ChildItem -LiteralPath (Join-Path $root 'src/OpenSynapse.App') -Recurse -Filter *.xaml |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
    ForEach-Object {
        $xaml = Get-Content -LiteralPath $_.FullName -Raw
        foreach ($m in [regex]::Matches($xaml, '\{?(?:x:Bind|Binding)\s+(?:Path=)?(?<expr>[A-Za-z_][\w\.]*)')) {
            $segments = $m.Groups['expr'].Value.Split('.')
            [void]$boundNames.Add($segments[$segments.Count - 1])
        }
    }

function Get-ClassAt {
    param([string[]]$Lines, [int]$Index)
    for ($i = $Index; $i -ge 0; $i--) {
        $m = [regex]::Match($Lines[$i], $classPattern)
        if ($m.Success) { return $m.Groups['name'].Value }
    }
    return '<unknown>'
}

function Remove-StringLiterals {
    param([string]$Line)
    $line = [regex]::Replace($Line, '"(?:\\.|[^"\\])*"', '""')
    return [regex]::Replace($line, "'(?:\\.|[^'\\])*'", "''")
}

function Get-MethodEnd {
    param([string[]]$Lines, [int]$Start)
    $depth = 0
    $opened = $false
    $expressionBody = $false
    for ($i = $Start; $i -lt $Lines.Count; $i++) {
        $raw = $Lines[$i]
        $line = Remove-StringLiterals $raw
        if (-not $opened) {
            $arrow = $line.IndexOf('=>')
            $brace = $line.IndexOf('{')
            if ($arrow -ge 0 -and ($brace -lt 0 -or $arrow -lt $brace)) {
                $expressionBody = $true
                $opened = $true
            }
            elseif ($brace -ge 0) {
                $opened = $true
            }
        }
        if ($expressionBody) {
            if ($raw.TrimEnd().EndsWith(';')) { return $i }
            continue
        }
        if (-not $opened) { continue }
        foreach ($ch in $line.ToCharArray()) {
            if ($ch -eq '{') { $depth++ }
            elseif ($ch -eq '}') { $depth-- }
        }
        if ($depth -le 0 -and $i -gt $Start) { return $i }
        if ($depth -le 0 -and $i -eq $Start -and $line.Contains('}')) { return $i }
    }
    return $Start
}

function Get-Dependencies {
    param([string]$Expression)
    $clean = Remove-StringLiterals $Expression
    $clean = [regex]::Replace($clean, '//.*$', '')
    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($m in [regex]::Matches($clean, '\b[A-Za-z_]\w*\b')) {
        if (-not $excluded.Contains($m.Value)) { [void]$set.Add($m.Value) }
    }
    return @($set)
}

$properties = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$allProperties = [System.Collections.Generic.List[object]]::new()
$methods = [System.Collections.Generic.List[object]]::new()
$assignments = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$setters = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$readOnlyFields = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$immutableMembers = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($file in $files) {
    $lines = Get-Content -LiteralPath $file.FullName
    if ($lines.Count -eq 0) { continue }
    $relative = $file.FullName.Replace("$root\", '').Replace('\', '/')
    $fileProperties = [System.Collections.Generic.List[object]]::new()

    # Expression-bodied properties (multi-line expressions are joined).
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $m = [regex]::Match($lines[$i], $propertyPattern)
        if (-not $m.Success) { continue }
        $expr = $m.Groups['expr'].Value
        $j = $i
        while (-not $expr.TrimEnd().EndsWith(';') -and $j + 1 -lt $lines.Count) {
            $j++
            $expr += ' ' + $lines[$j].Trim()
        }
        $class = Get-ClassAt -Lines $lines -Index $i
        $entry = [pscustomobject]@{
            File = $relative
            Class = $class
            Name = $m.Groups['name'].Value
            Access = $m.Groups['access'].Value
            Expression = $expr
            Line = $i + 1
        }
        $allProperties.Add($entry)
        $fileProperties.Add($entry)
        $key = "$class|$($entry.Name)"
        if (-not $properties.ContainsKey($key)) { $properties[$key] = $entry }
    }

    # Read-only fields are fixed for the object's lifetime; skip their ctor writes.
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $fm = [regex]::Match($lines[$i], '^\s*(?:public|private|internal|protected)\s+(?:(?:static|readonly|const)\s+)*(?<type>[A-Za-z_][\w<>\[\],\.\?]*)\s+(?<name>[A-Za-z_]\w*)\s*[;=]')
        if ($fm.Success -and $lines[$i] -match '\breadonly\b') {
            [void]$readOnlyFields.Add("$(Get-ClassAt -Lines $lines -Index $i)|$($fm.Groups['name'].Value)")
        }
        $gm = [regex]::Match($lines[$i], '^\s*(?:public|private|internal|protected)\s+(?:(?:static|sealed|override|virtual|new|required|unsafe)\s+)*(?<type>[A-Za-z_][\w<>\[\],\.\?]*)\s+(?<name>[A-Za-z_]\w*)\s*\{\s*get\s*;\s*\}')
        if ($gm.Success) {
            [void]$immutableMembers.Add("$(Get-ClassAt -Lines $lines -Index $i)|$($gm.Groups['name'].Value)")
        }
    }

    # Block-bodied properties: their setter is the notification point for
    # assignments like `IsBusy = false` from arbitrary methods.
    for ($i = 0; $i -lt $lines.Count; $i++) {
        # One-line property with an expression-bodied setter, e.g.
        # `public string ErrorText { get => _errorText; private set => SetField(...); }`
        $inline = [regex]::Match(
            $lines[$i],
            '^\s*(?:public|private|internal|protected)\s+(?:(?:static|sealed|override|virtual|new|required|unsafe)\s+)*(?<type>[A-Za-z_][\w<>\[\],\.\?]*)\s+(?<name>[A-Za-z_]\w*)\s*\{(?<body>.*)\}\s*$')
        if ($inline.Success -and $inline.Groups['body'].Value -match '\bset\b') {
            $body = $inline.Groups['body'].Value
            $notified = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($n in [regex]::Matches($body, 'nameof\((?<name>[A-Za-z_]\w*)\)')) {
                [void]$notified.Add($n.Groups['name'].Value)
            }
            $called = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($c in [regex]::Matches($body, '\b(?<name>[A-Za-z_]\w*)\s*\(')) {
                [void]$called.Add($c.Groups['name'].Value)
            }
            $setters["$(Get-ClassAt -Lines $lines -Index $i)|$($inline.Groups['name'].Value)"] = [pscustomobject]@{
                Body = $body
                Broadcast = ($body -match 'NotifyStateChanged\(\)|OnPropertyChanged\(string\.Empty\)|OnPropertyChanged\(null\)')
                Notified = $notified
                Called = $called
            }
            continue
        }
        if ($lines[$i] -match '=>') { continue }
        $pm = [regex]::Match($lines[$i], '^\s*(?:public|private|internal|protected)\s+(?:(?:static|sealed|override|virtual|new|required|unsafe)\s+)*(?<type>[A-Za-z_][\w<>\[\],\.\?]*)\s+(?<name>[A-Za-z_]\w*)\s*(?<brace>\{)?\s*$')
        if (-not $pm.Success) { continue }
        $open = if ($pm.Groups['brace'].Success) { $i } elseif ($i + 1 -lt $lines.Count -and $lines[$i + 1].TrimStart().StartsWith('{')) { $i + 1 } else { -1 }
        if ($open -lt 0) { continue }
        $end = Get-MethodEnd -Lines $lines -Start $open
        $body = ($lines[$open..$end] -join "`n")
        if ($body -notmatch '\bset\b') { continue }
        $notified = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($n in [regex]::Matches($body, 'nameof\((?<name>[A-Za-z_]\w*)\)')) {
            [void]$notified.Add($n.Groups['name'].Value)
        }
        $called = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($c in [regex]::Matches($body, '\b(?<name>[A-Za-z_]\w*)\s*\(')) {
            [void]$called.Add($c.Groups['name'].Value)
        }
        $setters["$(Get-ClassAt -Lines $lines -Index $i)|$($pm.Groups['name'].Value)"] = [pscustomobject]@{
            Body = $body
            Broadcast = ($body -match 'NotifyStateChanged\(\)|OnPropertyChanged\(string\.Empty\)|OnPropertyChanged\(null\)')
            Notified = $notified
            Called = $called
        }
        $i = $end
    }

    # Methods with their spans, bodies and notification facts.
    $fileMethods = [System.Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $m = [regex]::Match($lines[$i], $methodPattern)
        if (-not $m.Success) { continue }
        $end = Get-MethodEnd -Lines $lines -Start $i
        $body = ($lines[$i..$end] -join "`n")
        $class = Get-ClassAt -Lines $lines -Index $i
        $notified = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($n in [regex]::Matches($body, '(?:OnPropertyChanged|PropertyChanged\?\.Invoke)\([^\n;]*?nameof\((?<name>[A-Za-z_]\w*)\)')) {
            [void]$notified.Add($n.Groups['name'].Value)
        }
        $called = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($c in [regex]::Matches($body, '\b(?<name>[A-Za-z_]\w*)\s*\(')) {
            [void]$called.Add($c.Groups['name'].Value)
        }
        $method = [pscustomobject]@{
            File = $relative
            Class = $class
            Name = $m.Groups['name'].Value
            Start = $i
            End = $end
            Body = $body
            Broadcast = ($body -match 'NotifyStateChanged\(\)|OnPropertyChanged\(string\.Empty\)|OnPropertyChanged\(null\)|PropertyChanged\?\.Invoke\(this,\s*new\(string\.Empty\)\)')
            Notified = $notified
            Called = $called
            IsConstructor = ($m.Groups['name'].Value -eq $class)
            Index = $methods.Count
        }
        $fileMethods.Add($method)
        $methods.Add($method)
        $i = $end
    }

    # Assignments, mapped to the innermost method containing the line.
    $lineMethod = [int[]]::new($lines.Count)
    for ($i = 0; $i -lt $lineMethod.Count; $i++) { $lineMethod[$i] = -1 }
    foreach ($method in $fileMethods) {
        for ($i = $method.Start; $i -le $method.End -and $i -lt $lineMethod.Count; $i++) {
            $lineMethod[$i] = $method.Index
        }
    }
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lineMethod[$i] -lt 0) { continue }
        $line = $lines[$i]
        if ($line -match '=>' -or $line -match '^\s*//') { continue }
        $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($a in [regex]::Matches($line, '(?<![=!<>+\-*/%&|^])(?<name>[A-Za-z_]\w*)\s*=(?![=>])')) {
            [void]$names.Add($a.Groups['name'].Value)
        }
        foreach ($a in [regex]::Matches($line, 'SetField\(\s*ref\s+(?<name>[A-Za-z_]\w*)')) {
            [void]$names.Add($a.Groups['name'].Value)
        }
        foreach ($name in $names) {
            if (-not $assignments.ContainsKey($name)) { $assignments[$name] = [System.Collections.Generic.List[object]]::new() }
            $assignments[$name].Add([pscustomobject]@{ Method = $lineMethod[$i]; Line = $i + 1 })
        }
    }
}

$methodsByKey = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$callersByKey = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($method in $methods) {
    $key = "$($method.Class)|$($method.Name)"
    if (-not $methodsByKey.ContainsKey($key)) { $methodsByKey[$key] = [System.Collections.Generic.List[object]]::new() }
    $methodsByKey[$key].Add($method)
}
foreach ($method in $methods) {
    foreach ($called in $method.Called) {
        if ($called -eq $method.Name) { continue }
        $key = "$($method.Class)|$called"
        if (-not $callersByKey.ContainsKey($key)) { $callersByKey[$key] = [System.Collections.Generic.List[object]]::new() }
        $callersByKey[$key].Add($method)
    }
}

function Expand-Dependencies {
    param([string]$Class, [string[]]$Names, [int]$Depth = 0, [System.Collections.Generic.HashSet[string]]$Seen = $null)
    if ($null -eq $Seen) { $Seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
    if ($Depth -gt 6) { return @() }
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $Names) {
        $key = "$Class|$name"
        if ($properties.ContainsKey($key) -and $properties[$key].Expression -ne $null -and $Seen.Add($key)) {
            $inner = Get-Dependencies -Expression $properties[$key].Expression
            foreach ($dep in Expand-Dependencies -Class $Class -Names $inner -Depth ($Depth + 1) -Seen $Seen) {
                if (-not $result.Contains($dep)) { $result.Add($dep) }
            }
        }
        elseif (-not $result.Contains($name)) {
            $result.Add($name)
        }
    }
    return @($result)
}

function Test-MethodNotifies {
    param([object]$Method, [string]$Property, [int]$Depth = 0,
        [System.Collections.Generic.HashSet[string]]$Visited = $null)
    if ($null -eq $Visited) { $Visited = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }
    $visitKey = "$($Method.File)|$($Method.Start)|$Property"
    if (-not $Visited.Add($visitKey)) { return $false }
    if ($Method.Broadcast) { return $true }
    if ($Method.Notified.Contains($Property)) { return $true }
    if ($Depth -ge 2) { return $false }
    foreach ($called in $Method.Called) {
        $key = "$($Method.Class)|$called"
        if (-not $methodsByKey.ContainsKey($key)) { continue }
        foreach ($helper in $methodsByKey[$key]) {
            if (Test-MethodNotifies -Method $helper -Property $Property -Depth ($Depth + 1) -Visited $Visited) { return $true }
        }
    }
    foreach ($caller in $callersByKey["$($Method.Class)|$($Method.Name)"]) {
        if ($null -ne $caller -and
            (Test-MethodNotifies -Method $caller -Property $Property -Depth ($Depth + 1) -Visited $Visited)) {
            return $true
        }
    }
    return $false
}

function Test-SetterNotifies {
    param([string]$Class, [string]$Member, [string]$Property)
    $key = "$Class|$Member"
    if (-not $setters.ContainsKey($key)) { return $false }
    $setter = $setters[$key]
    if ($setter.Broadcast -or $setter.Notified.Contains($Property)) { return $true }
    foreach ($called in $setter.Called) {
        $methodKey = "$Class|$called"
        if (-not $methodsByKey.ContainsKey($methodKey)) { continue }
        foreach ($helper in $methodsByKey[$methodKey]) {
            if (Test-MethodNotifies -Method $helper -Property $Property) { return $true }
        }
    }
    return $false
}

$rows = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

foreach ($property in $allProperties) {
    if ($property.Access -ne 'public') { continue }
    if (-not $boundNames.Contains($property.Name)) { continue }
    $deps = Get-Dependencies -Expression $property.Expression
    $leafDeps = Expand-Dependencies -Class $property.Class -Names $deps
    foreach ($dep in $leafDeps | Sort-Object -Unique) {
        if ($dep -cnotmatch '^_|^[A-Z]') { continue }
        if ($readOnlyFields.Contains("$($property.Class)|$dep")) { continue }
        if ($immutableMembers.Contains("$($property.Class)|$dep")) { continue }
        if (-not $assignments.ContainsKey($dep)) { continue }
        foreach ($assignment in $assignments[$dep]) {
            $method = $methods[$assignment.Method]
            if ($method.Class -ne $property.Class) { continue }
            if ($method.IsConstructor) { continue }
            if ($method.Name -eq 'Dispose' -or $method.Name -eq 'DisposeAsync') { continue }
            $callers = $callersByKey["$($method.Class)|$($method.Name)"]
            if ($callers -and @($callers | Where-Object { -not $_.IsConstructor }).Count -eq 0) { continue }
            $rowKey = "$($property.File)|$($property.Class)|$($property.Name)|$dep|$($method.Name)|$($assignment.Line)"
            if (-not $seen.Add($rowKey)) { continue }
            $notified = (Test-MethodNotifies -Method $method -Property $property.Name) -or
                (Test-SetterNotifies -Class $property.Class -Member $dep -Property $property.Name) -or
                ($method.Body -match ('SetField\([^;]*ref\s+' + [regex]::Escape($dep) + '[^;]*nameof\(' + [regex]::Escape($property.Name) + '\)'))
            $rows.Add([pscustomobject]@{
                File = $property.File
                Class = $property.Class
                Property = $property.Name
                Dependency = $dep
                Method = $method.Name
                Line = $assignment.Line
                Notified = if ($notified) { 'yes' } else { 'no' }
            })
        }
    }
}

$directory = Split-Path -Parent $outputPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
$rows | Sort-Object File, Class, Property, Dependency, Method |
    Export-Csv -LiteralPath $outputPath -NoTypeInformation -Encoding utf8

$missing = @($rows | Where-Object Notified -eq 'no')
"Scanned $($allProperties.Count) expression-bodied properties, $($rows.Count) assignment rows."
"Missing notifications: $($missing.Count) -> $outputPath"
if ($missing.Count -gt 0) {
    $missing | Select-Object -First 20 | Format-Table File, Property, Dependency, Method, Line -AutoSize
}

if ($FailOnMissing -and $missing.Count -gt 0) { exit 1 }
exit 0
