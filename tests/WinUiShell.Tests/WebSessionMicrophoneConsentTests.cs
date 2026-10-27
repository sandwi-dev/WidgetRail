using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.WinUiShell.Tests;

[TestClass]
public sealed class WebSessionMicrophoneConsentTests
{
    private const string Origin="https://synthetic.fixture.invalid";
    private static Task<MicrophonePreference> Default(CancellationToken _) => Task.FromResult(MicrophonePreference.Default);
    [TestMethod]
    public async Task ExplicitGrantIsDocumentScopedAndReusedWithoutAnotherPrompt()
    {
        var consent=new WebSessionMicrophoneConsent();var prompts=0;
        Task<string?> Choose(CancellationToken _){prompts++;return Task.FromResult<string?>("once");}
        Assert.AreEqual(WebSessionMicrophoneState.Granted,(await consent.RequestAsync(Origin,1,()=>true,Default,Choose,default)).State);
        Assert.AreEqual(WebSessionMicrophoneState.Granted,(await consent.RequestAsync(Origin,1,()=>true,Default,Choose,default)).State);
        Assert.AreEqual(1,prompts);
        Assert.IsTrue(consent.IsGranted(Origin,1,out var remember));Assert.IsFalse(remember);
        Assert.IsFalse(consent.IsGranted("https://another.fixture.invalid",1,out _));
        Assert.IsFalse(consent.IsGranted(Origin,2,out _));
        consent.Retire();Assert.IsFalse(consent.IsGranted(Origin,1,out _));
        await consent.RequestAsync(Origin,2,()=>true,Default,Choose,default);Assert.AreEqual(2,prompts);
    }
    [TestMethod]
    public async Task ExistingProfileDecisionDoesNotPromptAndDenialOverridesDocumentGrant()
    {
        var consent=new WebSessionMicrophoneConsent();var prompts=0;
        Task<string?> Choose(CancellationToken _){prompts++;return Task.FromResult<string?>("allow");}
        var grant=await consent.RequestAsync(Origin,1,()=>true,_=>Task.FromResult(MicrophonePreference.Allow),Choose,default);
        Assert.AreEqual(new WebSessionMicrophoneResult(WebSessionMicrophoneState.Granted,true),grant);
        var deny=await consent.RequestAsync(Origin,1,()=>true,_=>Task.FromResult(MicrophonePreference.Deny),Choose,default);
        Assert.AreEqual(new WebSessionMicrophoneResult(WebSessionMicrophoneState.Denied,true),deny);
        Assert.AreEqual(0,prompts);Assert.IsFalse(consent.IsGranted(Origin,1,out _));
    }
    [TestMethod]
    public async Task CancelledIgnoringChooserCannotCreateALateRememberedGrant()
    {
        var consent=new WebSessionMicrophoneConsent();using var cancellation=new CancellationTokenSource();
        var answer=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=consent.RequestAsync(Origin,1,()=>true,Default,_=>answer.Task,cancellation.Token);
        cancellation.Cancel();
        Assert.AreEqual(WebSessionMicrophoneState.Cancelled,(await pending.WaitAsync(TimeSpan.FromSeconds(1))).State);
        answer.SetResult("allow");await Task.Yield();
        Assert.IsFalse(consent.IsGranted(Origin,1,out _));
    }
    [TestMethod]
    public async Task NavigationRetiresPendingConsentAndOwnerLossRejectsCompletion()
    {
        var consent=new WebSessionMicrophoneConsent();
        var answer=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=consent.RequestAsync(Origin,1,()=>true,Default,_=>answer.Task,default);
        consent.Retire();
        Assert.AreEqual(WebSessionMicrophoneState.Cancelled,(await pending.WaitAsync(TimeSpan.FromSeconds(1))).State);
        answer.SetResult("allow");
        var current=true;answer=new(TaskCreationOptions.RunContinuationsAsynchronously);
        pending=consent.RequestAsync(Origin,2,()=>current,Default,_=>answer.Task,default);
        current=false;answer.SetResult("allow");
        Assert.AreEqual(WebSessionMicrophoneState.Cancelled,(await pending).State);
        Assert.IsFalse(consent.IsGranted(Origin,2,out _));
    }
    [TestMethod]
    public async Task PermissionRequestsAreBoundedAndRememberingWaitsForRealUse()
    {
        var consent=new WebSessionMicrophoneConsent();
        var answer=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=consent.RequestAsync(Origin,1,()=>true,Default,_=>answer.Task,default);
        var duplicate=await consent.RequestAsync(Origin,1,()=>true,Default,_=>Task.FromResult<string?>("allow"),default);
        Assert.AreEqual(WebSessionMicrophoneState.Unavailable,duplicate.State);
        answer.SetResult("allow");
        Assert.AreEqual(new WebSessionMicrophoneResult(WebSessionMicrophoneState.Granted,true),await pending);
        Assert.IsTrue(consent.IsGranted(Origin,1,out var remember));Assert.IsTrue(remember);
        consent.Retire();Assert.IsFalse(consent.IsGranted(Origin,1,out _));
    }
}
