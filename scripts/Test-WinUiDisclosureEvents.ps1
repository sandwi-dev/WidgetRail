[CmdletBinding()]
param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
# Run in Windows PowerShell so UIAutomationClient comes from the installed
# Windows framework. No extra packages, hooks, or changes to the target process.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -ReferencedAssemblies @([System.Windows.Automation.AutomationElement].Assembly.Location,
    [System.Windows.Automation.AutomationProperty].Assembly.Location) -TypeDefinition @"
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;
public static class DisclosureEventProbe {
    public static string[] Run(int processId) {
        string[] result = null; Exception failure = null;
        var worker = new Thread(() => {
            AutomationPropertyChangedEventHandler handler = null;
            AutomationElement header = null;
            try {
                var window = AutomationElement.FromHandle(Process.GetProcessById(processId).MainWindowHandle);
                header = window.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "Widget.disclosure.header"));
                if (header == null) throw new InvalidOperationException("Disclosure fixture header missing.");
                var pattern = (ExpandCollapsePattern)header.GetCurrentPattern(ExpandCollapsePattern.Pattern);
                pattern.Expand(); Wait(() => pattern.Current.ExpandCollapseState == ExpandCollapseState.Expanded);
                var values = new ConcurrentQueue<string>();
                handler = (sender, args) => values.Enqueue(args.NewValue is int
                    ? ((ExpandCollapseState)(int)args.NewValue).ToString() : args.NewValue.ToString());
                Automation.AddAutomationPropertyChangedEventHandler(header, TreeScope.Element, handler,
                    ExpandCollapsePattern.ExpandCollapseStateProperty);
                try {
                    pattern.Collapse(); Wait(() => Array.IndexOf(values.ToArray(), "Collapsed") >= 0);
                    pattern.Expand(); Wait(() => Array.IndexOf(values.ToArray(), "Expanded") >= 0);
                    var before = values.Count;
                    pattern.Expand(); Thread.Sleep(200);
                    if (values.Count != before) throw new InvalidOperationException("No-op Expand raised another state event.");
                    result = values.ToArray();
                } catch (Exception error) {
                    throw new InvalidOperationException("State=" + pattern.Current.ExpandCollapseState + "; events=" + string.Join(",", values.ToArray()), error);
                } finally { Automation.RemoveAutomationPropertyChangedEventHandler(header, handler); }
            } catch (Exception error) { failure = error; }
        });
        worker.SetApartmentState(ApartmentState.MTA); worker.IsBackground = true; worker.Start();
        if (!worker.Join(10000)) throw new TimeoutException("UIA disclosure event probe timed out.");
        if (failure != null) throw new InvalidOperationException("UIA disclosure event probe failed.", failure);
        return result;
    }
    private static void Wait(Func<bool> condition) {
        var timer = Stopwatch.StartNew();
        while (!condition()) {
            if (timer.ElapsedMilliseconds > 3000) throw new TimeoutException("Expected UIA state/event was not received.");
            Thread.Sleep(20);
        }
    }
}
"@
try { $events = [DisclosureEventProbe]::Run($AppPid) }
catch { Write-Output $_.Exception.ToString(); exit 1 }
@{ passed = $true; events = @($events); duplicateExpandRaisedEvent = $false } |
    ConvertTo-Json | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output "Disclosure UIA property events passed: $($events -join ', ')."
