param([Parameter(Mandatory)][string]$EditorPath,[string]$PythonExe,[switch]$Build)
$ErrorActionPreference='Stop'
$labRepo=Split-Path -Parent $PSScriptRoot
$labWorkspace=Split-Path -Parent $labRepo
$labProject=Join-Path $labRepo 'ManhattanViewer'
$labWork=Join-Path $labWorkspace 'work/traversal-setup';New-Item -ItemType Directory -Path $labWork -Force|Out-Null
if($PythonExe){
 & $PythonExe -m pip install -r (Join-Path $labRepo 'training/requirements-lock.txt')
 if($LASTEXITCODE-ne 0){throw 'Pinned Python dependencies did not install.'}
}
function Invoke-LabEditor([string]$method,[string]$log) {
 $p=Start-Process -FilePath $EditorPath -WindowStyle Hidden -PassThru -ArgumentList @('-batchmode','-projectPath',('"'+$labProject+'"'),'-executeMethod',$method,'-logFile',('"'+$log+'"'))
 try {if(-not$p.WaitForExit(1800000)){throw 'Owned lab setup timed out'};if($p.ExitCode-ne 0){throw "Lab setup failed; see $log"}}
 finally {if(-not$p.HasExited){$p.Kill()}}
}
Invoke-LabEditor 'Manhattan.TraversalLab.Editor.TraversalLabPackages.Install' (Join-Path $labWork 'packages.log')
if($Build){Invoke-LabEditor 'Manhattan.TraversalLab.Editor.TraversalLabBuild.BuildAgent' (Join-Path $labWork 'build.log')}
