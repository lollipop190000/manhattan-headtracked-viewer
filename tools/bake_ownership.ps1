param(
    [Parameter(Mandatory)][string]$CatalogPath,
    [string]$SkylineId,
    [string]$ValidationOutput,
    [string]$EditorPath
)
. (Join-Path $PSScriptRoot 'common.ps1')
$CatalogPath = (Resolve-Path -LiteralPath $CatalogPath -ErrorAction Stop).Path
if ([bool]$SkylineId -ne [bool]$ValidationOutput) {
    throw 'A group validation requires both -SkylineId and -ValidationOutput.'
}
if ($ValidationOutput) { $ValidationOutput = [System.IO.Path]::GetFullPath($ValidationOutput) }
$names = @('MANHATTAN_OWNERSHIP_CATALOG_PATH', 'MANHATTAN_OWNERSHIP_VALIDATE_SKYLINE_ID', 'MANHATTAN_OWNERSHIP_VALIDATION_OUTPUT')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:MANHATTAN_OWNERSHIP_CATALOG_PATH = $CatalogPath
    [Environment]::SetEnvironmentVariable('MANHATTAN_OWNERSHIP_VALIDATE_SKYLINE_ID', $SkylineId, 'Process')
    [Environment]::SetEnvironmentVariable('MANHATTAN_OWNERSHIP_VALIDATION_OUTPUT', $ValidationOutput, 'Process')
    Invoke-ViewerOwnedEditorMethod -Method 'Manhattan.Editor.SkylineOwnershipBake.FromEnvironment' -EditorPath $EditorPath -Timeout 7200
}
finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
