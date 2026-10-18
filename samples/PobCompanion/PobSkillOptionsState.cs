namespace WidgetRail.Samples.PobCompanion;

internal sealed record PobSkillContext(string? Document,string Xml,int Group);
internal sealed record PobSkillRead(long Revision,PobSkillContext Context);

/// <summary>Owns the one-build, 32-group option cache and latest read/failure lifetime. Caller serializes access.</summary>
internal sealed class PobSkillOptionsState
{
    private readonly Dictionary<int,PobSkillOptions> cache=[];
    private string? document,xml;
    private PobSkillContext? selected;
    private PobSkillContext? failed;
    private PobSkillContext? retry;
    private PobSkillRead? pending;
    private long revision;
    internal int CachedCount=>cache.Count;
    internal PobSkillRead? Pending=>pending;
    internal void Select(PobSkillContext context)
    {
        if(document!=context.Document||xml!=context.Xml)
        {cache.Clear();document=context.Document;xml=context.Xml;failed=null;retry=null;pending=null;revision++;}
        if(selected!=context){selected=context;pending=null;revision++;}
    }
    internal PobSkillOptions? Current(PobSkillContext context)=>selected==context&&document==context.Document&&xml==context.Xml?cache.GetValueOrDefault(context.Group):null;
    internal bool Failed(PobSkillContext context)=>failed==context;
    internal bool UseSnapshot(PobSkillContext context)=>retry!=context;
    internal bool Reading(PobSkillContext context)=>pending?.Context==context;
    internal PobSkillRead? Begin(PobSkillContext context)
    {
        Select(context);
        if(Current(context)is not null||Failed(context)||Reading(context))return null;
        return pending=new(++revision,context);
    }
    internal bool Complete(PobSkillRead read,PobSkillOptions value)
    {
        if(pending!=read||selected!=read.Context)return false;
        Publish(read.Context,value);return true;
    }
    internal bool Fail(PobSkillRead read)
    {
        if(pending!=read||selected!=read.Context)return false;
        failed=read.Context;pending=null;return true;
    }
    internal void Publish(PobSkillContext context,PobSkillOptions value)
    {
        if(context.Group!=value.Group)throw new ArgumentException("Skill option group mismatch.",nameof(value));
        Select(context);
        if(!cache.ContainsKey(context.Group)&&cache.Count>=32)cache.Remove(cache.Keys.First());
        cache[context.Group]=value;failed=null;retry=null;pending=null;revision++;
    }
    internal void End(PobSkillRead read){if(pending==read)pending=null;}
    internal void Retry(PobSkillContext context){Select(context);if(failed==context)failed=null;retry=context;cache.Remove(context.Group);pending=null;revision++;}
    internal void Suspend(){pending=null;revision++;}
    internal void Clear(){cache.Clear();selected=null;document=null;xml=null;failed=null;retry=null;pending=null;revision++;}
}
