param(
 [ValidateSet('Install','Backup','Restore')][string]$Action='Install',
 [string]$ProjectPath,
 [string]$BackupPath,
 [switch]$WaitForEditor
)
$ErrorActionPreference='Stop'
$packageRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$ProjectPath) {
 Add-Type -AssemblyName System.Windows.Forms
 $dialog=New-Object System.Windows.Forms.FolderBrowserDialog
 $dialog.Description='移行するUnityプロジェクト（AssetsとProjectSettingsのあるフォルダー）を選択'
 if($dialog.ShowDialog() -ne 'OK'){exit 0}; $ProjectPath=$dialog.SelectedPath
}
$project=[IO.Path]::GetFullPath($ProjectPath).TrimEnd('\','/')
if(!(Test-Path -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt'))){throw 'Unityプロジェクトではありません。'}
$backupRoot=Join-Path (Split-Path $project -Parent) 'RI-Kai-Backups'
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$log=Join-Path $backupRoot 'migration.log'
function Log($message) { Add-Content -LiteralPath $log -Value "$(Get-Date -Format o) $message" -Encoding UTF8 }
function WriteJson($path,$data) { [IO.File]::WriteAllText($path,($data|ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false)) }
function CopyTree($source,$destination) {
 if(!(Test-Path -LiteralPath $source)){return}
 & robocopy $source $destination /E /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
 if($LASTEXITCODE -gt 7){throw "コピーに失敗しました: $source ($LASTEXITCODE)"}
}
function IsEditorOpen {
 @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($project,[StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -gt 0
}
function CheckChild($path) {
 $resolved=[IO.Path]::GetFullPath($path)
 if(!(($resolved+'\').StartsWith($project+'\',[StringComparison]::OrdinalIgnoreCase)) -or $resolved -eq $project){throw 'プロジェクト外への変更を拒否しました。'}
}
function BackupProject {
 $destination=Join-Path $backupRoot ((Split-Path $project -Leaf)+'_'+(Get-Date -Format 'yyyyMMdd_HHmmss_fff'))
 New-Item -ItemType Directory -Path $destination | Out-Null
 foreach($name in @('Assets','Packages','ProjectSettings','UserSettings')) { CopyTree (Join-Path $project $name) (Join-Path $destination $name) }
 WriteJson (Join-Path $destination 'backup.json') ([ordered]@{schema=1;project=$project;created=(Get-Date -Format o);complete=$true})
 Log "BACKUP OK $destination"
 return $destination
}
try {
 if($Action -ne 'Backup') {
  if($WaitForEditor){while(IsEditorOpen){Start-Sleep -Seconds 2}}
  elseif(IsEditorOpen){throw 'このプロジェクトのUnityを閉じてから実行してください。'}
 }
 if($Action -eq 'Backup'){ $saved=BackupProject; Write-Output $saved; exit 0 }
 if($Action -eq 'Restore') {
  if(!$BackupPath){
   Add-Type -AssemblyName System.Windows.Forms
   $dialog=New-Object System.Windows.Forms.FolderBrowserDialog
   $dialog.Description='backup.jsonのあるバックアップフォルダーを選択'
   if($dialog.ShowDialog() -ne 'OK'){exit 0}; $BackupPath=$dialog.SelectedPath
  }
  $backup=[IO.Path]::GetFullPath($BackupPath)
  $meta=Get-Content -LiteralPath (Join-Path $backup 'backup.json') -Raw -Encoding UTF8 | ConvertFrom-Json
  if(!$meta.complete -or $meta.project -ne $project){throw '別プロジェクトまたは未完了のバックアップです。'}
  foreach($name in @('Assets','Packages','ProjectSettings')) { if(!(Test-Path -LiteralPath (Join-Path $backup $name))){throw "バックアップが不完全です: $name"} }
  $before=BackupProject
  # Stage a complete restore before moving any active directory.
  $stage=Join-Path $project ('RI-Restore-'+[Guid]::NewGuid().ToString('N'))
  CheckChild $stage
  foreach($name in @('Assets','Packages','ProjectSettings','UserSettings')) { CopyTree (Join-Path $backup $name) (Join-Path $stage $name) }
  $swapped=@()
  try {
   foreach($name in @('Assets','Packages','ProjectSettings','UserSettings')) {
    $active=Join-Path $project $name; $staged=Join-Path $stage $name
    if(!(Test-Path -LiteralPath $staged)){continue}
    CheckChild $active; CheckChild $staged
    if(Test-Path -LiteralPath $active){Move-Item -LiteralPath $active -Destination (Join-Path $before ($name+'.displaced'))}
    $swapped += $name
    Move-Item -LiteralPath $staged -Destination $active
   }
  } catch {
   foreach($name in $swapped) {
    $active=Join-Path $project $name; $displaced=Join-Path $before ($name+'.displaced')
    CheckChild $active
    if(Test-Path -LiteralPath $active){Move-Item -LiteralPath $active -Destination (Join-Path $stage ($name+'.failed'))}
    if(Test-Path -LiteralPath $displaced){Move-Item -LiteralPath $displaced -Destination $active}
   }
   throw
  }
  Log "RESTORE OK $backup (before restore: $before)"; exit 0
 }
 $expectedTarget=Join-Path $project 'Packages/net.l1zerox.radialinventory'
 if([IO.Path]::GetFullPath($packageRoot).TrimEnd('\') -eq [IO.Path]::GetFullPath($expectedTarget).TrimEnd('\')){throw '更新元はプロジェクト外の配布フォルダーを指定してください。'}
 $sdkFile=Join-Path $project 'Packages/com.vrchat.avatars/package.json'
 if(!(Test-Path -LiteralPath $sdkFile)){throw 'VRChat Avatars SDKのあるプロジェクトを指定してください。'}
 $sdk=Get-Content -LiteralPath $sdkFile -Raw -Encoding UTF8|ConvertFrom-Json
 $sdkVersion=[version]($sdk.version -split '-')[0]
 if($sdkVersion -lt [version]'3.10.3' -or $sdkVersion -ge [version]'3.11.0'){throw "対応SDKは3.10.3以上3.11.0未満です（現在 $($sdk.version)）。VCCで対応版を用意してください。"}
 $legacy=Join-Path $project 'Assets/YagihataItems/RadialInventorySystemV4'
 if((Test-Path -LiteralPath $legacy) -and @(Get-ChildItem -LiteralPath $legacy -Filter '*.cs' -File -Recurse).Count -gt 0){throw 'Assets内に旧版のソースが残っています。VPMパッケージ版から移行してください。生成設定はそのまま保持しています。'}
 $saved=BackupProject
 $old=Join-Path $project 'Packages/yagihata.radialinventorysystem.v4'
 $new=Join-Path $project 'Packages/net.l1zerox.radialinventory'
 CheckChild $old; CheckChild $new
 if(Test-Path -LiteralPath $old){Move-Item -LiteralPath $old -Destination (Join-Path $saved 'RemovedOriginal')}
 if(Test-Path -LiteralPath $new){Move-Item -LiteralPath $new -Destination (Join-Path $saved 'PreviousFork')}
 try {
  CopyTree $packageRoot $new
  $path=Join-Path $project 'Packages/vpm-manifest.json'
  $manifest=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
  foreach($section in @('dependencies','locked')){
   if(!$manifest.$section){$manifest|Add-Member $section ([pscustomobject]@{})}
   $manifest.$section.PSObject.Properties.Remove('yagihata.radialinventorysystem.v4')
   $value=[pscustomobject]@{version='1.8.0'}
   if($section -eq 'locked'){$value|Add-Member dependencies ([pscustomobject]@{'com.vrchat.avatars'='>=3.10.3 <3.11.0'})}
   $manifest.$section|Add-Member 'net.l1zerox.radialinventory' $value -Force
  }
  WriteJson $path $manifest
  $upmPath=Join-Path $project 'Packages/manifest.json'
  $upm=Get-Content -LiteralPath $upmPath -Raw -Encoding UTF8|ConvertFrom-Json
  $upm.dependencies.PSObject.Properties.Remove('yagihata.radialinventorysystem.v4')
  WriteJson $upmPath $upm
  WriteJson (Join-Path $project 'RI-Kai-Migration.json') ([ordered]@{backup=$saved;version='1.8.0';date=(Get-Date -Format o)})
  Log "INSTALL OK $project (backup: $saved)"
 } catch {
  # Roll back the replaced package and manifests; preserve failed output for diagnosis.
  CheckChild $new
  if(Test-Path -LiteralPath $new){Move-Item -LiteralPath $new -Destination (Join-Path $saved 'FailedFork')}
  if(Test-Path -LiteralPath (Join-Path $saved 'RemovedOriginal')){Move-Item -LiteralPath (Join-Path $saved 'RemovedOriginal') -Destination $old}
  if(Test-Path -LiteralPath (Join-Path $saved 'PreviousFork')){Move-Item -LiteralPath (Join-Path $saved 'PreviousFork') -Destination $new}
  Copy-Item -LiteralPath (Join-Path $saved 'Packages/vpm-manifest.json') -Destination (Join-Path $project 'Packages/vpm-manifest.json') -Force
  Copy-Item -LiteralPath (Join-Path $saved 'Packages/manifest.json') -Destination (Join-Path $project 'Packages/manifest.json') -Force
  throw
 }
} catch { Log "FAILED $($_.Exception.Message)"; Write-Error $_; exit 1 }
