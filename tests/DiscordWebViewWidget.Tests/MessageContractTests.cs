using WidgetRail.Samples.DiscordWebView.Domain;

internal static class MessageContractTests
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new DiscordMessagingState("1","ready",true,true,true,2000,50,MessageRevision:7,ObservedSinceRevision:7);
        state.Validate("1");
        Reject(()=>(state with{ObservedSinceRevision=8}).Validate("1"),"Observation watermark cannot claim unseen future message revisions");
        Reject(()=>(state with{ObservedSinceRevision=-1}).Validate("1"),"Observation watermark must be a nonnegative provider revision");
        var message=new DiscordMessage("30","20","1","Sender",null,"Message\nsecond line",1700000000000,null,"123",[],true);
        var page=new DiscordMessagePage(new("1","20",null,"30",false),[message]);
        page.Validate("1","20",null,50);
        check(message.IsValid("20") && (message with{Content=new string('a',4000)}).IsValid("20"),
            "Native message contract preserves multiline and premium-length incoming text");
        check(!(message with{Content=new string('a',4001)}).IsValid("20") && !(message with{Content="bad\0body"}).IsValid("20"),
            "Oversized and invalid native message bodies fail closed");
        Reject(()=>page.Validate("2","20",null,50),"History cannot cross an account boundary");
        Reject(()=>page.Validate("1","21",null,50),"History cannot cross a conversation boundary");
        Reject(()=>(page with{Items=[message,message]}).Validate("1","20",null,50),"Duplicate message identities cannot be published");
        Reject(()=>(page with{Context=new("1","20","30","30",true)}).Validate("1","20","30",50),
            "An unchanged older-message cursor cannot cause an endless paging loop");
        var attachment=new DiscordMessageAttachment("40","clip.mp4","video/mp4",500000000,
            "https://cdn.discordapp.com/attachments/20/40/clip.mp4?signature=example");
        check(attachment.IsValid,"Large attachment metadata does not allocate the file contents");
        check(!(attachment with{Url="file:///C:/private.txt"}).IsValid &&
            !(attachment with{Url="https://cdn.discordapp.com.evil.invalid/clip"}).IsValid &&
            !(attachment with{Url="https://user:password@cdn.discordapp.com/clip"}).IsValid,
            "Message attachment metadata cannot introduce a local file or an unapproved URL");
        var sent=new DiscordMessageSendResult("1","20","123","sent",message,null);
        sent.Validate("1","20","123");
        Reject(()=>sent.Validate("1","20","456"),"Send acknowledgment must match the exact request nonce");
        Reject(()=>(sent with{Message=null}).Validate("1","20","123"),"A missing message is not a confirmed send");
        (sent with{Status="unknown",Message=null}).Validate("1","20","123");
        check(true,"Ambiguous send remains distinguishable from confirmed delivery");
        void Reject(Action action,string name)
        {
            try{action();}
            catch(InvalidDataException){check(true,name);return;}
            check(false,name);
        }
    }
}
