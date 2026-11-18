using WidgetRail.Samples.DiscordWebView.Domain;
using WidgetRail.WidgetProtocol;

internal static class DiscordPendingMessagesTests
{
    internal static void Run(Action<bool,string> check)
    {
        var first=new DiscordPendingMessageKey("1",new string('a',32),"10");
        var second=first with{ChannelId="20"};
        var file=new WidgetFileReference(new string('b',32),"capture.png","image/png",50,1000000);
        var journal=new DiscordPendingMessages();
        WidgetFileReference[] mutable=[file];
        check(journal.TryAdmit(first,"123","First draft",mutable,out var admitted),"A pending Discord send is admitted once for its exact owner and conversation");
        mutable[0]=file with{Name="changed.png"};
        check(admitted!.Files[0].Name=="capture.png" && admitted.Content=="First draft","Pending submission freezes the original content and file metadata");
        var immutable=false;
        try{((IList<WidgetFileReference>)admitted.Files)[0]=mutable[0];}catch(NotSupportedException){immutable=true;}
        check(immutable,"Callers cannot mutate the journal's held attachment list");
        check(!journal.TryAdmit(first,"124","Replacement draft",[],out var existing) && ReferenceEquals(existing,admitted),
            "An unresolved send cannot be overwritten by a later send to the same recipient");
        check(journal.TryAdmit(second,"456","Other recipient",[],out _) && journal.TryGet(first,out existing) && existing!.Nonce=="123",
            "Other recipients remain usable while the original attempt survives navigation");
        check(!journal.TryGet(first with{Account="2"},out _) && !journal.TryGet(first with{SessionId=new string('c',32)},out _),
            "Pending send lookup never crosses account or backend-session identity");
        check(!journal.TryResolve(first,"wrong",out _) && journal.Count==2,"A late acknowledgment for another nonce cannot remove the current attempt");
        check(journal.TryResolve(first,"123",out var resolved) && ReferenceEquals(resolved,admitted) && journal.Count==1,
            "Exact terminal acknowledgment returns the held record for deterministic file cleanup");
        check(journal.TryAdmit(first,"789","New draft",[],out _) && !journal.TryResolve(first,"123",out _) && journal.TryGet(first,out existing) && existing!.Nonce=="789",
            "A delayed old acknowledgment cannot remove a replacement attempt");

        var saturated=new DiscordPendingMessages();
        for(var index=0;index<DiscordPendingMessages.MaximumAttempts;index++)
            if(!saturated.TryAdmit(first with{ChannelId=(100+index).ToString()},(1000+index).ToString(),"Pending",[],out _))
                throw new InvalidOperationException("Journal filled early.");
        check(!saturated.TryAdmit(second,"9999","Overflow",[],out var rejected) && rejected is null && saturated.Count==16,
            "Sixteen unresolved attempts remain bounded without evicting uncertainty");
        check(saturated.TryGet(first with{ChannelId="100"},out existing) && existing!.Nonce=="1000","Capacity pressure preserves the oldest unresolved nonce");
        saturated.TryResolve(first with{ChannelId="100"},"1000",out _);
        check(saturated.TryAdmit(second,"9999","Now admitted",[],out _),"An exact resolution releases journal capacity");

        var retiring=new DiscordPendingMessages();
        retiring.TryAdmit(first,"1","",[file],out _);
        retiring.TryAdmit(second,"2","",[file],out _);
        check(retiring.HoldsFile(file.Id),"A held attachment grant can be excluded from ordinary draft cleanup");
        retiring.TryResolve(first,"1",out _);
        check(retiring.HoldsFile(file.Id),"Resolving one attempt does not release a file grant still held by another recipient");
        retiring.TryAdmit(first,"1","",[file],out _);
        var otherOwner=first with{Account="2",SessionId=new string('d',32)};
        var otherFile=file with{Id=new string('e',32)};
        retiring.TryAdmit(otherOwner,"3","",[otherFile],out _);
        var released=retiring.Reset(otherOwner.Account,otherOwner.SessionId);
        check(released.Count==1 && released[0].Id==file.Id && retiring.Count==1 && retiring.TryGet(otherOwner,out _),
            "Account-session reset retires old attempts and returns each abandoned file grant once");
        check(!retiring.HoldsFile(file.Id) && retiring.HoldsFile(otherFile.Id),"Reset removes retired grant ownership while preserving the current owner's grant");
        check(retiring.Reset(otherOwner.Account,otherOwner.SessionId).Count==0 && retiring.Count==1,"Repeated owner reconciliation preserves current unresolved attempts");
        check(retiring.Clear().Single().Id==otherFile.Id && retiring.Count==0 && retiring.Clear().Count==0,
            "Widget teardown returns remaining grants once and empties the journal");

        var concurrent=new DiscordPendingMessages();var successes=0;
        Parallel.For(0,32,index=>{if(concurrent.TryAdmit(first,(index+1).ToString(),"Concurrent",[],out _))Interlocked.Increment(ref successes);});
        check(successes==1 && concurrent.Count==1,"Concurrent same-recipient admissions preserve exactly one unresolved attempt");
        Reject(()=>journal.TryAdmit(first,"not-a-nonce","Text",[],out _),"Pending journal rejects malformed nonces");
        Reject(()=>journal.TryAdmit(first,"1",new string('x',2001),[],out _),"Pending journal does not retain oversized outgoing text");
        Reject(()=>journal.TryAdmit(first,"1","",[],out _),"Empty submissions cannot consume journal capacity");
        Reject(()=>journal.TryAdmit(first,"1","Files",[file,file],out _),"Duplicate file grants are rejected before being held");
        void Reject(Action action,string name)
        {try{action();check(false,name);}catch(ArgumentException){check(true,name);}}
    }
}
