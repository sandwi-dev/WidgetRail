using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    /// <summary>Exercise the actual owner handler against its live synthetic fixture entry.
    /// Extra request IDs have no broker waiter; none of the injected opens reaches navigation.</summary>
    internal async Task VerifyHostedStartupOwnershipForValidationAsync(string widgetId, Action<bool, string> check)
    {
        var original = entries.Single(pair => pair.Key.Widget == widgetId && pair.Value.Hosted is not null);
        var before = CreatedCount;
        var identity = new BrokerWidgetIdentity("fixture.web-session", "fixture.publisher", original.Value.Authority.WidgetInstanceId);
        var definition = new WebSessionDefinition("startup-validation", "Synthetic startup", WebSessionSource.Remote("https://startup.fixture.invalid"));
        var savedRegister = RegisterHostedProfileForValidation;
        var savedConfigure = BeforeHostedConfigureForValidation;
        try
        {
            await HandleHostedWorkAsync(Work(original.Value.Hosted!, WebSessionOperation.Open));
            check(ReferenceEquals(entries.GetValueOrDefault(original.Key), original.Value) && original.Value.Surface.IsReady,
                "A rejected duplicate Open cannot retire another attempt's live browser");

            var delayedReference = new WebSessionReference(Guid.NewGuid().ToString("N"));
            var registered = new TaskCompletionSource<BrowserProfileRecord?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RegisterHostedProfileForValidation = (_, _, _) => { entered.TrySetResult(); return registered.Task; };
            var delayed = HandleHostedWorkAsync(Work(delayedReference, WebSessionOperation.Open));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await HandleHostedWorkAsync(Work(delayedReference, WebSessionOperation.Close));
            registered.SetResult(new(original.Value.Surface.ProfileName, null)); // Deliberately ignores the cancelled token.
            await delayed.WaitAsync(TimeSpan.FromSeconds(5));
            check(CreatedCount == before && !entries.ContainsKey((widgetId, delayedReference.Id)) &&
                ReferenceEquals(entries.GetValueOrDefault(original.Key), original.Value),
                "Close during profile registration rejects its late result without creating or retiring another browser");

            BrowserSurface? allocated = null;
            RegisterHostedProfileForValidation = (_, _, _) => Task.FromResult<BrowserProfileRecord?>(new(original.Value.Surface.ProfileName, null));
            BeforeHostedConfigureForValidation = surface => { allocated = surface; throw new InvalidDataException("Synthetic configuration failure."); };
            var failedReference = new WebSessionReference(Guid.NewGuid().ToString("N"));
            await HandleHostedWorkAsync(Work(failedReference, WebSessionOperation.Open));
            check(allocated?.IsRetiredForValidation == true && !entries.ContainsKey((widgetId, failedReference.Id)) &&
                ReferenceEquals(entries.GetValueOrDefault(original.Key), original.Value) && original.Value.Surface.IsReady,
                "Configuration failure disposes the unregistered native surface and preserves the existing session");
            check(BrowserResidencyCount == 1 && CreatedCount == before,
                "Rejected and cancelled startup attempts release their capacity reservations");
        }
        finally { RegisterHostedProfileForValidation = savedRegister; BeforeHostedConfigureForValidation = savedConfigure; }

        BridgeWebSessionWork Work(WebSessionReference reference, WebSessionOperation operation) => new(
            new HostWebSessionWork(Guid.NewGuid().ToString("N"), widgetId, identity, reference,
                new(operation) { Session = operation == WebSessionOperation.Close ? reference : null, Definition = operation == WebSessionOperation.Open ? definition : null }),
            Array.Empty<BridgeWebSessionResource>());
    }
}
