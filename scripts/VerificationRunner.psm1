Set-StrictMode -Version Latest

if ($null -eq ('WidgetRail.Verification.CappedStreamPump' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace WidgetRail.Verification
{
    public sealed class PumpResult
    {
        public bool Truncated { get; init; }
        public long BytesWritten { get; init; }
    }

    public static class CappedStreamPump
    {
        private static readonly byte[] Marker = new UTF8Encoding(false).GetBytes(
            "\n[output truncated by verification runner]\n");

        public static async Task<PumpResult> PumpAsync(
            StreamReader reader,
            string path,
            long maximumBytes)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Output path is required.", nameof(path));
            if (maximumBytes < Marker.Length) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            var encoding = new UTF8Encoding(false);
            var characters = new char[4096];
            var bytes = new byte[encoding.GetMaxByteCount(characters.Length)];
            long written = 0;
            var truncated = false;
            using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            while (true)
            {
                var read = await reader.ReadAsync(characters, 0, characters.Length).ConfigureAwait(false);
                if (read == 0) break;
                var byteCount = encoding.GetBytes(characters, 0, read, bytes, 0);
                if (!truncated && written + byteCount <= maximumBytes - Marker.Length)
                {
                    await output.WriteAsync(bytes, 0, byteCount).ConfigureAwait(false);
                    written += byteCount;
                }
                else
                {
                    truncated = true;
                }
            }
            if (truncated)
            {
                await output.WriteAsync(Marker, 0, Marker.Length).ConfigureAwait(false);
                written += Marker.Length;
            }
            await output.FlushAsync().ConfigureAwait(false);
            return new PumpResult { Truncated = truncated, BytesWritten = written };
        }
    }

    public sealed class ProcessJob : IDisposable
    {
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private IntPtr handle;

        public ProcessJob(System.Diagnostics.Process process, string name)
        {
            if (process == null) throw new ArgumentNullException(nameof(process));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Job name is required.", nameof(name));
            handle = CreateJobObject(IntPtr.Zero, name);
            if (handle == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            var information = new JobObjectExtendedLimitInformation();
            information.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
            var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(information, pointer, false);
                if (!SetInformationJobObject(handle, 9, pointer, (uint)size))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if (!AssignProcessToJobObject(handle, process.Handle))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            catch
            {
                CloseHandle(handle);
                handle = IntPtr.Zero;
                throw;
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
        }

        public void Dispose()
        {
            var current = handle;
            handle = IntPtr.Zero;
            if (current != IntPtr.Zero) CloseHandle(current);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job,
            int informationClass,
            IntPtr information,
            uint informationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
'@
}

function Invoke-BoundedVerificationProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]{1,80}$')][string]$Id,
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][string]$OutputDirectory,
        [ValidateRange(4096, 67108864)][int]$MaximumOutputBytes = 4194304,
        [switch]$SuppressReplay
    )

    if ($TimeoutSeconds -lt 1) { throw 'TimeoutSeconds must be positive.' }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $stdoutPath = Join-Path $OutputDirectory "$Id.stdout.log"
    $stderrPath = Join-Path $OutputDirectory "$Id.stderr.log"
    $requestPath = Join-Path $OutputDirectory "$Id.command.json"
    $launcherPath = Join-Path $PSScriptRoot 'Invoke-VerificationChild.ps1'
    if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
        throw 'Verification child launcher is missing.'
    }
    [IO.File]::WriteAllText(
        $requestPath,
        ([ordered]@{ file = $FilePath; arguments = @($ArgumentList) } | ConvertTo-Json -Depth 4 -Compress),
        [Text.UTF8Encoding]::new($false))
    $startEventName = "Local\WidgetRailVerification-$([Guid]::NewGuid().ToString('N'))"
    $jobName = "Local\WidgetRailVerificationJob-$([Guid]::NewGuid().ToString('N'))"
    $startEvent = [Threading.EventWaitHandle]::new(
        $false, [Threading.EventResetMode]::ManualReset, $startEventName)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'pwsh'
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Environment['WRAIL_VERIFICATION_JOB'] = $jobName
    foreach ($argument in @('-NoProfile', '-File', $launcherPath, '-RequestPath', $requestPath,
            '-StartEventName', $startEventName)) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $startedUtc = [DateTimeOffset]::UtcNow
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    $started = $false
    $job = $null
    try {
        if (-not $process.Start()) { throw "Verification step '$Id' did not start." }
        $started = $true
        $job = [WidgetRail.Verification.ProcessJob]::new($process, $jobName)
        $stdoutTask = [WidgetRail.Verification.CappedStreamPump]::PumpAsync(
            $process.StandardOutput, $stdoutPath, $MaximumOutputBytes)
        $stderrTask = [WidgetRail.Verification.CappedStreamPump]::PumpAsync(
            $process.StandardError, $stderrPath, $MaximumOutputBytes)
        [void]$startEvent.Set()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $timedOut = $true
            $job.Dispose()
            $job = $null
            try { $process.Kill($true) } catch { }
            if (-not $process.WaitForExit(5000)) {
                throw "Verification step '$Id' did not terminate within five seconds after timeout."
            }
        }
        else {
            $job.Dispose()
            $job = $null
        }
        $pumpTasks = [Threading.Tasks.Task[]]@($stdoutTask, $stderrTask)
        $allPumps = [Threading.Tasks.Task]::WhenAll($pumpTasks)
        if (-not $allPumps.Wait(5000)) {
            $process.StandardOutput.Dispose()
            $process.StandardError.Dispose()
            throw "Verification step '$Id' output pumps did not finish within five seconds."
        }
        $stdoutResult = $stdoutTask.GetAwaiter().GetResult()
        $stderrResult = $stderrTask.GetAwaiter().GetResult()
        if (-not $SuppressReplay -and $stdoutResult.BytesWritten -ne 0) {
            [Console]::Out.Write([IO.File]::ReadAllText($stdoutPath))
        }
        if (-not $SuppressReplay -and $stderrResult.BytesWritten -ne 0) {
            [Console]::Error.Write([IO.File]::ReadAllText($stderrPath))
        }
        $exitCode = if ($timedOut) { $null } else { $process.ExitCode }
        [pscustomobject]@{
            id = $Id
            description = $Description
            command = @($FilePath) + @($ArgumentList)
            startedUtc = $startedUtc.ToString('O')
            durationMilliseconds = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
            timeoutSeconds = $TimeoutSeconds
            status = if ($timedOut) { 'timed_out' } elseif ($exitCode -eq 0) { 'passed' } else { 'failed' }
            exitCode = $exitCode
            stdoutLog = [System.IO.Path]::GetFileName($stdoutPath)
            stderrLog = [System.IO.Path]::GetFileName($stderrPath)
            stdoutBytes = $stdoutResult.BytesWritten
            stderrBytes = $stderrResult.BytesWritten
            stdoutTruncated = $stdoutResult.Truncated
            stderrTruncated = $stderrResult.Truncated
        }
    }
    finally {
        $stopwatch.Stop()
        if ($null -ne $job) { $job.Dispose() }
        $startEvent.Dispose()
        if ($started) {
            try {
                if (-not $process.HasExited) {
                    $process.Kill($true)
                    [void]$process.WaitForExit(5000)
                }
            }
            catch { }
        }
        $process.Dispose()
    }
}

function Write-VerificationJUnit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Result,
        [Parameter(Mandatory)][string]$StdoutPath,
        [Parameter(Mandatory)][string]$StderrPath,
        [Parameter(Mandatory)][string]$OutputPath,
        [ValidateRange(1, 10000)][int]$MaximumCases = 10000
    )

    $cases = [System.Collections.Generic.List[object]]::new()
    $caseExtractionTruncated = $false
    if (Test-Path -LiteralPath $StdoutPath) {
        $reader = [IO.File]::OpenText($StdoutPath)
        try {
            while (($line = $reader.ReadLine()) -ne $null) {
                if ($line.Length -gt 8192) { $line = $line.Substring(0, 8192) }
                if ($line -match '^PASS\s+(.+)$') {
                    if ($cases.Count -eq $MaximumCases) { $caseExtractionTruncated = $true; break }
                    $cases.Add([pscustomobject]@{ name = $Matches[1]; failure = $null })
                }
                elseif ($line -match '^FAIL\s+([^:]+):?\s*(.*)$') {
                    if ($cases.Count -eq $MaximumCases) { $caseExtractionTruncated = $true; break }
                    $cases.Add([pscustomobject]@{ name = $Matches[1]; failure = $Matches[2] })
                }
                elseif ($line -match '^passed\s+(.+?)\s+\([^)]+\)$') {
                    if ($cases.Count -eq $MaximumCases) { $caseExtractionTruncated = $true; break }
                    $cases.Add([pscustomobject]@{ name = $Matches[1]; failure = $null })
                }
                elseif ($line -match '^failed\s+(.+?)\s+\([^)]+\)$') {
                    if ($cases.Count -eq $MaximumCases) { $caseExtractionTruncated = $true; break }
                    $cases.Add([pscustomobject]@{
                        name = $Matches[1]
                        failure = 'MSTest case failed; inspect retained stdout for assertion details.'
                    })
                }
            }
        }
        finally { $reader.Dispose() }
    }
    if ($caseExtractionTruncated) {
        $cases.Add([pscustomobject]@{
            name = '[case extraction truncated]'
            failure = 'Console case extraction exceeded its configured limit.'
        })
    }
    if ($cases.Count -eq 0) {
        $failure = if ($Result.status -eq 'passed') { $null } elseif ($Result.status -eq 'timed_out') {
            "Step exceeded $($Result.timeoutSeconds) seconds."
        } else { "Step exited with code $($Result.exitCode); inspect the retained stderr log." }
        $cases.Add([pscustomobject]@{ name = $Result.description; failure = $failure })
    }
    elseif ($Result.status -ne 'passed' -and
            @($cases | Where-Object { $null -ne $_.failure }).Count -eq 0) {
        $failure = if ($Result.status -eq 'timed_out') {
            "Step exceeded $($Result.timeoutSeconds) seconds."
        } else { "Step exited with code $($Result.exitCode); inspect the retained stderr log." }
        $cases.Add([pscustomobject]@{ name = '[process result]'; failure = $failure })
    }

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Indent = $true
    $settings.Encoding = [Text.UTF8Encoding]::new($false)
    $writer = [System.Xml.XmlWriter]::Create($OutputPath, $settings)
    try {
        $failures = @($cases | Where-Object { $null -ne $_.failure }).Count
        $writer.WriteStartDocument()
        $writer.WriteStartElement('testsuite')
        $writer.WriteAttributeString('name', $Result.id)
        $writer.WriteAttributeString('tests', $cases.Count.ToString([Globalization.CultureInfo]::InvariantCulture))
        $writer.WriteAttributeString('failures', $failures.ToString([Globalization.CultureInfo]::InvariantCulture))
        $writer.WriteAttributeString('time', ($Result.durationMilliseconds / 1000).ToString('0.000', [Globalization.CultureInfo]::InvariantCulture))
        foreach ($case in $cases) {
            $writer.WriteStartElement('testcase')
            $writer.WriteAttributeString('classname', $Result.id)
            $writer.WriteAttributeString('name', $case.name)
            if ($null -ne $case.failure) {
                $writer.WriteStartElement('failure')
                $writer.WriteString([string]$case.failure)
                $writer.WriteEndElement()
            }
            $writer.WriteEndElement()
        }
        if ((Test-Path -LiteralPath $StderrPath) -and
            (Get-Item -LiteralPath $StderrPath).Length -ne 0) {
            $writer.WriteStartElement('system-err')
            $writer.WriteString("See retained stderr log: $([IO.Path]::GetFileName($StderrPath))")
            $writer.WriteEndElement()
        }
        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    }
    finally { $writer.Dispose() }
}

function Get-RemainingVerificationTimeout {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateRange(1, 7200)][int]$RequestedSeconds,
        [Parameter(Mandatory)][ValidateRange(0, 86400)][double]$ElapsedSeconds,
        [Parameter(Mandatory)][ValidateRange(1, 7200)][int]$OverallTimeoutSeconds
    )

    $remainingSeconds = [Math]::Floor($OverallTimeoutSeconds - $ElapsedSeconds)
    if ($remainingSeconds -lt 1) {
        throw "Verification exceeded its $OverallTimeoutSeconds-second overall limit."
    }
    [Math]::Min($RequestedSeconds, [int]$remainingSeconds)
}

function Enter-RepositoryVerificationLease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][ValidatePattern('^[0-9A-Za-z-]{1,80}$')][string]$RunId,
        [Parameter(Mandatory)][ValidateSet('Debug', 'Release')][string]$Configuration,
        [Parameter(Mandatory)][DateTimeOffset]$StartedUtc,
        [ValidateRange(0, 300)][int]$WaitSeconds = 0
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot)
    $leaseDirectory = Join-Path $root 'artifacts\verification'
    New-Item -ItemType Directory -Path $leaseDirectory -Force | Out-Null
    $leasePath = Join-Path $leaseDirectory '.repository-run.lock'
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($WaitSeconds)
    while ($true) {
        $stream = $null
        try {
            # Readers may inspect owner metadata, but no second writer can open
            # this live handle. Process death releases the OS lock; file bytes
            # are deliberately never treated as lock authority.
            $stream = [IO.FileStream]::new(
                $leasePath,
                [IO.FileMode]::OpenOrCreate,
                [IO.FileAccess]::ReadWrite,
                [IO.FileShare]::Read)
            $metadata = [ordered]@{
                processId = [Environment]::ProcessId
                runId = $RunId
                configuration = $Configuration
                startedUtc = $StartedUtc.ToString('O')
            } | ConvertTo-Json -Compress
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($metadata)
            $stream.SetLength(0)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
            return [pscustomobject]@{
                path = $leasePath
                stream = $stream
                metadata = $metadata
            }
        }
        catch [IO.IOException] {
            if ($null -ne $stream) { $stream.Dispose() }
            if ([DateTimeOffset]::UtcNow -ge $deadline) {
                $owner = try {
                    $ownerStream = [IO.FileStream]::new(
                        $leasePath,
                        [IO.FileMode]::Open,
                        [IO.FileAccess]::Read,
                        [IO.FileShare]::ReadWrite)
                    try {
                        $reader = [IO.StreamReader]::new($ownerStream, [Text.Encoding]::UTF8)
                        try { $reader.ReadToEnd() } finally { $reader.Dispose() }
                    }
                    finally { $ownerStream.Dispose() }
                }
                catch { '[unavailable]' }
                if ($owner.Length -gt 1024) { $owner = $owner.Substring(0, 1024) }
                throw [InvalidOperationException]::new(
                    "verification_lease_busy: Another verification run owns this checkout. Owner: $owner")
            }
            Start-Sleep -Milliseconds 50
        }
        catch {
            if ($null -ne $stream) { $stream.Dispose() }
            throw
        }
    }
}

function Exit-RepositoryVerificationLease {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Lease)

    if ($null -ne $Lease.stream) {
        $Lease.stream.Dispose()
        $Lease.stream = $null
    }
}

function Get-VerificationEvidenceEligibility {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][bool]$VerificationPassed,
        [Parameter(Mandatory)][string]$StartingCommit,
        [AllowEmptyString()][string]$StartingStatus = '',
        [Parameter(Mandatory)][bool]$StartingStatusTruncated,
        [Parameter(Mandatory)][bool]$FinalProvenanceSucceeded,
        [AllowEmptyString()][string]$FinishedCommit = '',
        [AllowEmptyString()][string]$FinishedStatus = '',
        [Parameter(Mandatory)][bool]$FinishedStatusTruncated
    )

    $reasons = [Collections.Generic.List[string]]::new()
    if (-not $VerificationPassed) { $reasons.Add('verification_failed') }
    if ($StartingStatusTruncated) { $reasons.Add('starting_status_truncated') }
    if (-not [string]::IsNullOrEmpty($StartingStatus)) {
        $reasons.Add('starting_worktree_dirty')
    }
    if (-not $FinalProvenanceSucceeded) {
        $reasons.Add('final_provenance_failed')
    }
    else {
        if ($FinishedStatusTruncated) { $reasons.Add('finished_status_truncated') }
        if (-not [string]::IsNullOrEmpty($FinishedStatus)) {
            $reasons.Add('finished_worktree_dirty')
        }
        if ($StartingCommit -ne $FinishedCommit) {
            $reasons.Add('repository_commit_changed')
        }
        if ($StartingStatus -ne $FinishedStatus) {
            $reasons.Add('repository_status_changed')
        }
    }
    $stable = $FinalProvenanceSucceeded -and
        -not $StartingStatusTruncated -and
        -not $FinishedStatusTruncated -and
        $StartingCommit -eq $FinishedCommit -and
        $StartingStatus -eq $FinishedStatus
    [pscustomobject]@{
        eligible = $reasons.Count -eq 0
        repositoryStateStable = $stable
        reasons = @($reasons)
    }
}

Export-ModuleMember -Function Invoke-BoundedVerificationProcess, Write-VerificationJUnit, `
    Get-RemainingVerificationTimeout, Enter-RepositoryVerificationLease, `
    Exit-RepositoryVerificationLease, Get-VerificationEvidenceEligibility
