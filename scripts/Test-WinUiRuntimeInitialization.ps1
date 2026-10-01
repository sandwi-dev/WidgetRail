[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AssemblyPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ProjectPath,
    [ValidatePattern('^[A-Za-z0-9._-]+$')][string]$Configuration='Release',
    [ValidatePattern('^[A-Za-z0-9._-]+$')][string]$Platform='x64',
    [ValidatePattern('^[A-Za-z0-9._-]+$')][string]$RuntimeIdentifier='win-x64'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
# Read PE metadata and IL only. Never Assembly.Load/LoadFrom the frontend: doing
# so could execute its module initializer and change runtime package registration.
$assembly=[IO.Path]::GetFullPath($AssemblyPath)
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(-not (Test-Path -LiteralPath $assembly -PathType Leaf)){throw 'Frontend assembly does not exist'}
if(Test-Path -LiteralPath $output){throw 'Choose a fresh initialization-evidence directory'}
if($ProjectPath){$ProjectPath=[IO.Path]::GetFullPath($ProjectPath);if(-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)){throw 'Frontend project does not exist'}}
New-Item -ItemType Directory -Path $output|Out-Null

if(-not ('WidgetRail.Validation.RuntimeInitializerReader' -as [type])){
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace WidgetRail.Validation
{
    public sealed class InitializerCall
    {
        public int Offset { get; set; }
        public string Opcode { get; set; }
        public string Target { get; set; }
        public string Token { get; set; }
        public bool PrecededByTrueConstant { get; set; }
    }
    public sealed class InitializerMethod
    {
        public string Name { get; set; }
        public string Token { get; set; }
        public string IlHex { get; set; }
        public List<InitializerCall> Calls { get; set; } = new();
    }
    public sealed class InitializerEvidence
    {
        public string AssemblyName { get; set; }
        public string ModuleVersionId { get; set; }
        public string EntryPoint { get; set; }
        public List<InitializerMethod> Methods { get; set; } = new();
    }
    public static class RuntimeInitializerReader
    {
        private static readonly Dictionary<ushort, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null))
            .ToDictionary(op => unchecked((ushort)op.Value));

        private static string TypeName(MetadataReader metadata, EntityHandle handle)
        {
            if(handle.Kind == HandleKind.TypeDefinition)
            {
                var type = metadata.GetTypeDefinition((TypeDefinitionHandle)handle);
                var ns = metadata.GetString(type.Namespace);
                return (ns.Length == 0 ? "" : ns + ".") + metadata.GetString(type.Name);
            }
            if(handle.Kind == HandleKind.TypeReference)
            {
                var type = metadata.GetTypeReference((TypeReferenceHandle)handle);
                var ns = metadata.GetString(type.Namespace);
                return (ns.Length == 0 ? "" : ns + ".") + metadata.GetString(type.Name);
            }
            return handle.Kind.ToString();
        }
        private static string MethodName(MetadataReader metadata, int token)
        {
            var handle = MetadataTokens.EntityHandle(token);
            if(handle.Kind == HandleKind.MethodDefinition)
            {
                var method = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return TypeName(metadata, method.GetDeclaringType()) + "." + metadata.GetString(method.Name);
            }
            if(handle.Kind == HandleKind.MemberReference)
            {
                var method = metadata.GetMemberReference((MemberReferenceHandle)handle);
                return TypeName(metadata, method.Parent) + "." + metadata.GetString(method.Name);
            }
            if(handle.Kind == HandleKind.MethodSpecification)
                return MethodName(metadata, MetadataTokens.GetToken(metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method));
            throw new BadImageFormatException("Unexpected method token kind: " + handle.Kind);
        }
        private static InitializerMethod ReadMethod(PEReader pe, MetadataReader metadata, MethodDefinitionHandle handle)
        {
            var method = metadata.GetMethodDefinition(handle);
            if(method.RelativeVirtualAddress == 0) throw new BadImageFormatException("Initializer has no IL body");
            var bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
            var result = new InitializerMethod { Name = MethodName(metadata, MetadataTokens.GetToken(handle)),
                Token = $"0x{MetadataTokens.GetToken(handle):X8}", IlHex = Convert.ToHexString(bytes) };
            string previousOpcode = null;
            for(int position = 0; position < bytes.Length;)
            {
                int offset = position;
                ushort code = bytes[position++];
                if(code == 0xFE)
                {
                    if(position >= bytes.Length) throw new BadImageFormatException("Truncated IL opcode");
                    code = (ushort)(0xFE00 | bytes[position++]);
                }
                if(!Opcodes.TryGetValue(code, out var opcode)) throw new BadImageFormatException("Unknown IL opcode");
                int operandLength;
                switch(opcode.OperandType)
                {
                    case OperandType.InlineNone: operandLength = 0; break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: operandLength = 1; break;
                    case OperandType.InlineVar: operandLength = 2; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: operandLength = 8; break;
                    case OperandType.InlineSwitch:
                        if(position > bytes.Length - 4) throw new BadImageFormatException("Truncated IL switch");
                        int count = BitConverter.ToInt32(bytes, position);
                        if(count < 0) throw new BadImageFormatException("Invalid IL switch");
                        operandLength = checked(4 + count * 4); break;
                    default: operandLength = 4; break;
                }
                if(operandLength > bytes.Length - position) throw new BadImageFormatException("Truncated IL operand");
                if(opcode == OpCodes.Call || opcode == OpCodes.Callvirt || opcode == OpCodes.Newobj)
                {
                    int token = BitConverter.ToInt32(bytes, position);
                    result.Calls.Add(new InitializerCall { Offset = offset, Opcode = opcode.Name,
                        Token = $"0x{token:X8}", Target = MethodName(metadata, token),
                        PrecededByTrueConstant = previousOpcode == "ldc.i4.1" });
                }
                previousOpcode = opcode.Name;
                position += operandLength;
            }
            return result;
        }
        public static InitializerEvidence Read(string path)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if(!pe.HasMetadata || pe.PEHeaders.CorHeader == null) throw new BadImageFormatException("Expected a managed frontend PE");
            var metadata = pe.GetMetadataReader();
            var result = new InitializerEvidence { AssemblyName = metadata.GetString(metadata.GetAssemblyDefinition().Name),
                ModuleVersionId = metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString(),
                EntryPoint = MethodName(metadata, pe.PEHeaders.CorHeader.EntryPointTokenOrRelativeVirtualAddress) };
            foreach(var handle in metadata.TypeDefinitions)
            {
                var name = TypeName(metadata, handle);
                if(name != "<Module>" && name != "Microsoft.Windows.ApplicationModel.WindowsAppRuntime.Common.AutoInitialize" &&
                    name != "Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentManagerCS.AutoInitialize") continue;
                foreach(var method in metadata.GetTypeDefinition(handle).GetMethods())
                {
                    if(metadata.GetString(metadata.GetMethodDefinition(method).Name) == ".ctor") continue;
                    result.Methods.Add(ReadMethod(pe, metadata, method));
                }
            }
            return result;
        }
    }
}
'@
}

$evidence=[WidgetRail.Validation.RuntimeInitializerReader]::Read($assembly)
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Name){if(-not $Condition){throw $Name};$checks.Add($Name)}
function Method([string]$Name){
    $matches=@($evidence.Methods|Where-Object Name -CEQ $Name)
    if($matches.Count -ne 1){throw "Expected exactly one initializer method: $Name"}
    return $matches[0]
}
function Calls($Method,[string]$Target){return @($Method.Calls|Where-Object {$_.Opcode -in @('call','callvirt') -and $_.Target -ceq $Target}).Count -eq 1}
$common='Microsoft.Windows.ApplicationModel.WindowsAppRuntime.Common.AutoInitialize.InitializeWindowsAppSDK'
$deployment='Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentManagerCS.AutoInitialize.AccessWindowsAppSDK'
$options='Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentManagerCS.AutoInitialize.get_Options'
Check ($evidence.AssemblyName -ceq 'OverlayFrontend.WinUI') 'Assembly is the frontend'
Check ($evidence.EntryPoint -ceq 'WidgetRail.OverlayFrontend.WinUI.Program.Main') 'Managed entrypoint is Program.Main'
Check (Calls (Method '<Module>..cctor') $common) 'Module initializer calls InitializeWindowsAppSDK before Program.Main'
Check (Calls (Method $common) $deployment) 'Windows App SDK initializer calls deployment auto-initializer'
Check (Calls (Method $deployment) 'Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentManager.Initialize') 'Deployment auto-initializer calls DeploymentManager.Initialize'
Check (Calls (Method $deployment) $options) 'Deployment auto-initializer obtains its generated options'
$showUi=@((Method $options).Calls|Where-Object {$_.Target -ceq 'Microsoft.Windows.ApplicationModel.WindowsAppRuntime.DeploymentInitializeOptions.set_OnErrorShowUI'})
Check ($showUi.Count -eq 1 -and $showUi[0].PrecededByTrueConstant) 'Generated options set OnErrorShowUI=true'
Check (@((Method $common).Calls|Where-Object {$_.Target -match 'Bootstrap'}).Count -eq 0) 'Current module initializer does not call the bootstrap initializer'

$evaluation=$null
if($ProjectPath){
    $binlog=Join-Path $output ('evaluation-'+[guid]::NewGuid().ToString('N')+'.binlog')
    $properties='WindowsPackageType,WindowsAppSDKSelfContained,WindowsAppSDKBootstrapInitialize,WindowsAppSDKDeploymentManagerInitialize,WindowsAppSDKAutoInitialize,SelfContained,PublishTrimmed,DefineConstants'
    $arguments=@('msbuild',$ProjectPath,"-p:Configuration=$Configuration","-p:Platform=$Platform","-p:RuntimeIdentifier=$RuntimeIdentifier",
        "-getProperty:$properties",'-getItem:Compile',"-bl:$binlog;ProjectImports=None")
    # No target or restore switch: MSBuild only evaluates the project.
    $query=@(& dotnet @arguments 2>&1)
    $exitCode=$LASTEXITCODE
    $query|Set-Content -LiteralPath (Join-Path $output 'msbuild-evaluation.json')
    Check ($exitCode -eq 0 -and (Test-Path -LiteralPath $binlog -PathType Leaf)) 'MSBuild evaluation succeeded and retained its unique binlog'
    $evaluated=($query -join [Environment]::NewLine)|ConvertFrom-Json
    $props=$evaluated.Properties
    Check ($props.WindowsPackageType -ceq 'MSIX' -and $props.WindowsAppSDKSelfContained -ne 'true' -and
        $props.WindowsAppSDKDeploymentManagerInitialize -eq 'true' -and $props.WindowsAppSDKAutoInitialize -eq 'true') 'Current project enables packaged framework-dependent deployment initialization'
    Check ($props.WindowsAppSDKBootstrapInitialize -ne 'true' -and $props.DefineConstants -match '(^|;)MICROSOFT_WINDOWSAPPSDK_AUTOINITIALIZE_DEPLOYMENTMANAGER(;|$)') 'Current project defines the deployment initializer and does not enable bootstrap'
    $sources=@($evaluated.Items.Compile|Where-Object {$_.Identity -match 'AutoInitializer\.cs$'}|ForEach-Object {
        @{path=$_.FullPath;sha256=(Get-FileHash -LiteralPath $_.FullPath -Algorithm SHA256).Hash}
    })
    $evaluation=@{project=$ProjectPath;configuration=$Configuration;platform=$Platform;runtimeIdentifier=$RuntimeIdentifier;
        properties=$props;initializerCompileInputs=$sources;binlog=$binlog;binarySourceCorrespondenceVerified=$false}
}
$result=@{schemaVersion=1;passed=$true;assembly=$assembly;assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;
    inspection=$evidence;checks=$checks;currentProjectEvaluation=$evaluation;frontendExecuted=$false;
    runtimeDeploymentExecuted=$false;cleanMachineQualified=$false}
$result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $output 'result.json')
"Passed $($checks.Count) metadata/IL initialization checks. Frontend and deployment initializer were not executed."
