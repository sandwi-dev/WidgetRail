using System.Text.Json;
using WidgetRail.Samples.DiscordPrototype;

internal static class ActivityContractTests
{
    internal static int Run()
    {
        var count=0;
        void Pass(string label){Console.WriteLine("PASS "+label);count++;}
        var legacy=new DiscordState(1,true,"1",new(true,true,true,true,true,true),[],null,"",false,false,[],[],false,false,null,"closed");
        legacy.Validate();
        if(legacy.Activities.Length!=0||legacy.ActivityAvailability!="unavailable")throw new Exception("Legacy state defaults changed");
        Pass("Legacy Discord snapshots keep an empty unavailable activity feed");
        var row=new DiscordFriendActivity("2","Friend","online","playing","Game","", "20","10","Lobby","Server",true,"guild:10:20:2",false);
        var valid=legacy with{Activities=[row],ActivityAvailability="ready"};valid.Validate();
        var json=JsonSerializer.Serialize(valid,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundtrip=JsonSerializer.Deserialize<DiscordState>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web))!;roundtrip.Validate();
        if(roundtrip.Activities.Single()!=row)throw new Exception("Activity fields changed across JSON");
        Pass("Bounded friend activity round-trips through the domain contract");
        void Reject(DiscordState candidate,string label)
        {
            try{candidate.Validate();}catch(InvalidDataException){Pass(label);return;}
            throw new Exception("Accepted invalid activity: "+label);
        }
        Reject(valid with{Activities=[row,row]},"Duplicate friend row identity is rejected");
        Reject(valid with{Activities=Enumerable.Repeat(row,51).ToArray()},"Activity count over 50 is rejected");
        Reject(valid with{ActivityAvailability="unavailable"},"Unavailable state cannot retain previous friend data");
        Reject(valid with{Activities=[row with{CanWatch=true}]},"Watch cannot target another call");
        Reject(valid with{Activities=[row with{StreamKey="guild:10:20:9"}]},"Stream identity must match its friend and channel");
        Reject(valid with{Activities=[row with{GuildId=null}]},"Incomplete voice identity is rejected");
        Reject(valid with{Activities=[row with{Name="Friend\nprivate"}]},"Control characters cannot cross the activity contract");
        Reject(valid with{Activities=[row with{Title=new string('x',121)}]},"Oversized activity labels are rejected");
        Reject(valid with{Activities=[row with{Kind="custom"}]},"Unknown activity types are rejected");
        var avatar="https://cdn.discordapp.com/avatars/2/"+new string('a',32)+".png?size=64";
        (valid with{Activities=[row with{AvatarUrl=avatar}]}).Validate();
        (legacy with{Participants=[new("2","Friend",false,false,false,avatar)]}).Validate();
        Pass("Optional fixed64px friend and participant avatars validate");
        foreach(var unsafeAvatar in new[]{avatar.Replace("/2/","/3/"),avatar.Replace("cdn.discordapp.com","example.com"),avatar.Replace("size=64","size=4096"),avatar+"&token=secret"})
        {
            Reject(valid with{Activities=[row with{AvatarUrl=unsafeAvatar}]},"Activity avatar rejects a changed identity host or query");
            Reject(legacy with{Participants=[new("2","Friend",false,false,false,unsafeAvatar)]},"Participant avatar rejects a changed identity host or query");
        }
        return count;
    }
}
