param([Parameter(Mandatory)][string]$Player,[Parameter(Mandatory)][string]$PythonExe,[int]$Steps=300000,[int]$NumEnvs=4,[string]$RunPrefix)
$ErrorActionPreference='Stop'
$labRepo=Split-Path -Parent $PSScriptRoot
$labWorkspace=Split-Path -Parent $labRepo
$labOutput=Join-Path $labWorkspace 'outputs/TraversalTraining';New-Item -ItemType Directory -Path $labOutput -Force|Out-Null
if(-not$RunPrefix){$RunPrefix='flow-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')}
$labLearner=Join-Path (Split-Path -Parent $PythonExe) 'mlagents-learn.exe'
$labConfig=Join-Path $labOutput ($RunPrefix+'.yaml')
(Get-Content (Join-Path $labRepo 'training/traversal-ppo.yaml') -Raw).Replace('max_steps: 300000',"max_steps: $Steps")|Set-Content $labConfig
$labRuns=@()
foreach($mode in @('mixed','off','on')) {
 $runId=$RunPrefix+'-'+$mode
 $telemetry=Join-Path $labOutput ('telemetry/'+$runId);New-Item -ItemType Directory -Path $telemetry -Force|Out-Null
 & $labLearner $labConfig --env $Player --run-id $runId --results-dir $labOutput --num-envs $NumEnvs --seed 17 --torch-device cpu --no-graphics --env-args --traversal-agent --flow $mode --map-seed 1000 --telemetry-dir $telemetry -batchmode -nographics
 if($LASTEXITCODE-ne 0){throw "Training failed for $mode; no result was fabricated."}
 $labRuns+=@{condition=$mode;runId=$runId;model=(Join-Path $labOutput ($runId+'/TraversalFlow.onnx'));telemetry=$telemetry}
}
$labRuns|ConvertTo-Json -Depth 5|Set-Content (Join-Path $labOutput ($RunPrefix+'-models.json'))
Write-Host "TensorBoard: $PythonExe -m tensorboard.main --logdir $labOutput"
