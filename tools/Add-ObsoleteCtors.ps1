param(
    [Parameter(Mandatory)]
    [string]$FilePath,

    [Parameter(Mandatory)]
    [string]$Pattern,

    [Parameter(Mandatory)]
    [string]$AttributeText
)

$lines  = Get-Content -LiteralPath $FilePath
$result = [System.Collections.Generic.List[string]]::new($lines.Count + 100)

foreach ($line in $lines) {
    if ($line -match $Pattern) {
        $indent = [regex]::Match($line, '^\s*').Value
        $result.Add($indent + $AttributeText)
    }
    $result.Add($line)
}

Set-Content -LiteralPath $FilePath -Value $result -Encoding UTF8
Write-Host "Done. Lines before: $($lines.Count)  Lines after: $($result.Count)"
