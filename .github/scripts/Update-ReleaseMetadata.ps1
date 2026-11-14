param([Parameter(Mandatory = $true)][string]$Version)

$ErrorActionPreference = 'Stop'
$tag = $Version.Trim()
$match = [regex]::Match($tag, '\Av?((?:0|[1-9][0-9]*)(?:\.(?:0|[1-9][0-9]*)){2,3})\z')
if (!$match.Success) {
    throw 'Use a numeric version such as 4.0.2, v4.0.2, or 4.0.2.1.'
}
$version = $match.Groups[1].Value
foreach ($part in $version.Split('.')) {
    [uint32]$component = 0
    if (![uint32]::TryParse($part, [ref]$component) -or $component -gt 65534) {
        throw 'Each version component must be between 0 and 65534.'
    }
}

$year = [DateTime]::UtcNow.Year.ToString()
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$encoding = [Text.UTF8Encoding]::new($false, $true)
$files = [ordered]@{
    'TVTComment/TVTComment.csproj' = [ordered]@{
        '(<Version>)[^<\r\n]+(</Version>)' = $version
        '(2020-)[0-9]{4}( noriokun4649</Copyright>)' = $year
    }
    'TVTComment/Views/ShellContents/BasicSettingControl.xaml' = [ordered]@{
        '(2020-)[0-9]{4}( noriokun4649)' = $year
    }
    'Viewer/Viewer.cpp' = [ordered]@{
        '(pInfo->pszDescription\s*=\s*L"[^\r\n"]*\(v)[0-9.]+(\)";)' = $version
        '(2022-)[0-9]{4}( noriokun4649")' = $year
    }
    'LICENSE' = [ordered]@{
        '(2020-)[0-9]{4}( noriokun4649)' = $year
    }
}
$updates = [ordered]@{}
foreach ($file in $files.GetEnumerator()) {
    $path = Join-Path $root $file.Key
    $text = $encoding.GetString([IO.File]::ReadAllBytes($path))
    foreach ($field in $file.Value.GetEnumerator()) {
        $pattern = [regex]::new($field.Key)
        $count = $pattern.Matches($text).Count
        if ($count -ne 1) {
            throw "Expected exactly one release metadata field in ${path}, found $count."
        }
        $value = $field.Value
        $text = $pattern.Replace($text, [Text.RegularExpressions.MatchEvaluator]{
            param($item)
            $item.Groups[1].Value + $value + $item.Groups[2].Value
        })
    }
    $updates[$path] = $text
}
foreach ($update in $updates.GetEnumerator()) {
    [IO.File]::WriteAllBytes($update.Key, $encoding.GetBytes($update.Value))
}

if ($env:GITHUB_OUTPUT) {
    [IO.File]::AppendAllText($env:GITHUB_OUTPUT, "version=$version`ntag=$tag`n", $encoding)
}
Write-Host "Release ${tag}: version $version, copyright year $year"
