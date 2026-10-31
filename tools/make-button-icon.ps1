# Draws a mod's BUTTON icon (its HUD button and its Unified UI button) from a list holding one Lucide
# icon, in make-icons.ps1's format: <name>.lucide.txt -> <name>.png beside it. White on clear, -Size
# pixels square (72 by default: twice the 36 px button, sharp at 4K), the glyph two thirds of it and
# centred -- Play It!'s and Render It's button icons, which sit on the game's round OptionBase sprites.
#
#   powershell -File <mod>\ModsLibCS1\tools\make-button-icon.ps1 -Spec <mod>\<project>\Resources\<Name>.lucide.txt
#
# Deterministic: run it again with nothing changed and git shows no diff. Look at the PNG before
# committing it, and credit the icon in the mod's own notices.

param(
    [Parameter(Mandatory = $true)][string]$Spec,
    [int]$Size = 72
)

$ErrorActionPreference = 'Stop'
$Spec = (Resolve-Path $Spec).Path
$file = [IO.Path]::GetFileName($Spec)
if ($file -notmatch '^(.+)\.lucide\.txt$') { throw "the list's name must end in .lucide.txt: $Spec" }
$png = Join-Path (Split-Path -Parent $Spec) "$($Matches[1]).png"

Add-Type -Path (Join-Path $PSScriptRoot 'SpriteMaker.cs') -ReferencedAssemblies System.Drawing

$before = if (Test-Path $png) { (Get-FileHash $png).Hash } else { '' }
$summary = [SpriteMaker]::RunButtonIcon($Spec, $png, $Size)
$after = (Get-FileHash $png).Hash
$state = if ($before -eq $after) { 'UNCHANGED' } else { 'changed' }
Write-Host "button icon: $summary -> $png ($state)"
