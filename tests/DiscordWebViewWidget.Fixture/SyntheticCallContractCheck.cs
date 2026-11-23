using System.Text.Json;
using WidgetRail.Samples.DiscordWebView.Domain;

internal static class SyntheticCallContractCheck
{
    internal static async Task RunAsync(string directory)
    {
        if(Directory.Exists(directory))throw new InvalidOperationException("Use a fresh call contract directory.");
        Directory.CreateDirectory(directory);
        await using var session=new SyntheticDiscordSession();
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var checks=new List<string>();
        var initial=await Read();
        Check(initial.Calls?.Incoming.Length==1&&initial.ChannelId is null,"Synthetic fixture begins with one valid incoming invitation and no call");
        await Command("callDecline","11",initial.Calls!.Incoming[0].Id);
        Check((await Read()).Calls!.Incoming.Length==0,"Decline removes only the synthetic invitation");
        await session.RequestMicrophoneAsync(deadline.Token);
        await Command("callStart","10");
        Check((await Read()).Calls!.Current?.Phase=="outgoing","Outgoing dispatch publishes valid connecting state");
        await Until(state=>state.Calls?.Current?.Phase=="active");
        Check(true,"Synthetic peer connection publishes valid active state");
        await session.InvokeAsync("leave",new{},deadline.Token);
        await Until(state=>state.Calls?.Incoming.Length==1);
        var incoming=(await Read()).Calls!.Incoming[0];
        Check(incoming.Id==new string('c',32),"Leave schedules the distinct background invitation");
        await session.RequestMicrophoneAsync(deadline.Token);
        await Command("callAnswer","11",incoming.Id);
        Check((await Read()).Calls!.Current is {Phase:"active",ChannelId:"11"},"Answer joins the exact invitation");
        await session.InvokeAsync("leave",new{},deadline.Token);
        Check((await Read()).Calls is {Current:null,Incoming.Length:0},"Final Leave returns to valid disconnected state without another ring");
        await File.WriteAllTextAsync(Path.Combine(directory,"result.json"),JsonSerializer.Serialize(new{passed=true,checks}));
        Console.WriteLine($"{checks.Count}/{checks.Count} synthetic call contract checks passed.");
        async Task<DiscordState> Read(){var value=await session.ReadAsync(deadline.Token);value.Validate();return value;}
        async Task Command(string method,string target,string? invitation=null)
        {
            var state=await Read();
            await session.InvokeAsync(method,new{account=state.Account,previousChannelId=state.ChannelId,channelId=target,
                invitationId=invitation,commandId=Guid.NewGuid().ToString("N")},deadline.Token);
        }
        async Task Until(Func<DiscordState,bool> condition){while(!condition(await Read()))await Task.Delay(20,deadline.Token);}
        void Check(bool valid,string message){if(!valid)throw new InvalidDataException(message);checks.Add(message);}
    }
}
