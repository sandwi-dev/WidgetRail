param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference='Stop'
$source=[IO.Path]::GetFullPath($Path)
$limit=32L*1024*1024
if((Get-Item -LiteralPath $source).Length -le $limit){return}
$inputStream=[IO.File]::OpenRead($source)
$length=$inputStream.Length;$part=0
try{
    $buffer=[byte[]]::new(1024*1024)
    while($inputStream.Position -lt $length){
        $outputStream=[IO.File]::Open($source+'.part'+$part,[IO.FileMode]::CreateNew)
        try{
            $remaining=$limit
            while($remaining -gt 0 -and ($read=$inputStream.Read($buffer,0,[int][Math]::Min($buffer.Length,$remaining))) -gt 0){
                $outputStream.Write($buffer,0,$read);$remaining-=$read
            }
        }finally{$outputStream.Dispose()}
        $part++
    }
}finally{$inputStream.Dispose()}
@{parts=$part;length=$length}|ConvertTo-Json -Compress|Set-Content -LiteralPath ($source+'.parts.json') -Encoding utf8
# Only the explicitly supplied archive is removed after all its parts exist.
Remove-Item -LiteralPath $source
