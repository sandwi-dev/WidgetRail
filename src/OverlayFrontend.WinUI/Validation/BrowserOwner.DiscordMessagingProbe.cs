using System.Text.Json;
using System.Text.Json.Nodes;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed partial class BrowserOwner
{
    internal async Task<JsonArray> ReadDiscordMessagingProbeAsync()
    {
        var rows=new JsonArray();
        foreach(var entry in entries.Values.Where(entry=>entry.Hosted is not null && entry.Surface.IsBackendSession).Take(4).ToArray())
            if(Uri.TryCreate(entry.Surface.CurrentUrl,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="discord.com")
                rows.Add(await entry.Surface.ReadDiscordMessagingProbeAsync());
        return rows;
    }
}

internal sealed partial class BrowserSurface
{
    internal async Task<JsonObject> ReadDiscordMessagingProbeAsync()
    {
        if(retired||core is null||!IsBackendSession||!Uri.TryCreate(core.Source,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="discord.com")
            return new(){["available"]=false};
        var arguments=JsonSerializer.Serialize(new{expression=DiscordMessagingProbeScript,awaitPromise=true,returnByValue=true});
        using var response=JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",arguments).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        if(!response.RootElement.TryGetProperty("result",out var result)||!result.TryGetProperty("value",out var value)||value.ValueKind!=JsonValueKind.Object)
            return new(){["available"]=false,["probeError"]="evaluation_unavailable"};
        var output=new JsonObject();
        foreach(var name in new[]{"available","hasFailure","restFound","shapeObserved","probeAttached"})
            output[name]=value.TryGetProperty(name,out var flag)&&flag.ValueKind==JsonValueKind.True;
        foreach(var (name,allowed) in new[]{
            ("method",new[]{"none","other","messageHistory","messageConversations","messageObserve","messageOpenDm","messageSend","messageReconcile","messagePrepareAttachments"}),
            ("type",new[]{"none","other","Error","TypeError","RangeError","SyntaxError"}),
            ("code",new[]{"none","unclassified","account_changed","messaging_retired","messaging_unavailable","invalid_conversation","conversation_unavailable",
                "invalid_page","invalid_message","invalid_message_response","rate_limited","message_forbidden","message_rejected","message_unconfirmed","message_updates_unavailable"})})
        {
            var text=value.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String?field.GetString():null;
            output[name]=allowed.Contains(text,StringComparer.Ordinal)?text:"other";
        }
        if(value.TryGetProperty("shape",out var shape)&&shape.ValueKind==JsonValueKind.Object)
        {
            var clean=new JsonObject();
            foreach(var property in shape.EnumerateObject().Take(64))
            {
                if(!DiscordMessagingShapeFields.Contains(property.Name,StringComparer.Ordinal))continue;
                if(property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)clean[property.Name]=property.Value.GetBoolean();
                else if(property.Value.ValueKind==JsonValueKind.Number&&property.Value.TryGetInt32(out var count)&&
                    count>=0&&count<=(property.Name=="httpStatus"?599:100))clean[property.Name]=count;
            }
            output["shape"]=clean;
        }
        if(value.TryGetProperty("apiFacts",out var facts)&&facts.ValueKind==JsonValueKind.Object)
        {
            var clean=new JsonObject();
            foreach(var name in new[]{"authenticatedModuleFound","sameAsGeneric","authenticatedGet","authenticatedPost","authenticatedDelete","authenticatedDel",
                "authenticatedBaseSameOrigin","authenticatedBaseApiPath","authenticatedBaseVersioned","genericBaseGetter","endpointMessagesFound","endpointRelative",
                "endpointVersioned","endpointAbsolute","apiEnvPresent","recentHistorySitePath","recentHistoryApiPath","recentHistorySameOrigin","objectObjectRequest",
                "deleteFacadeFound","deleteFacadeSameGeneric","deleteFacadeGet","deleteFacadePost","fetchMessagesFound","fetchUsesObjectGet"})
                clean[name]=facts.TryGetProperty(name,out var flag)&&flag.ValueKind==JsonValueKind.True;
            foreach(var name in new[]{"genericGetArity","genericPostArity","facadeGetArity","facadePostArity"})
                if(facts.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.Number&&field.TryGetInt32(out var count)&&count is >=0 and <=10)clean[name]=count;
            output["apiFacts"]=clean;
        }
        if(value.TryGetProperty("sources",out var sources)&&sources.ValueKind==JsonValueKind.Object)
        {
            var clean=new JsonObject();
            foreach(var name in new[]{"genericGet","facadeGet","messageFetch","messageFetchRequest"})
                if(sources.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String&&field.GetString() is {Length:<=4000} source &&
                    !source.Contains("https:",StringComparison.OrdinalIgnoreCase)&&!source.Contains("http:",StringComparison.OrdinalIgnoreCase))clean[name]=source;
            output["sources"]=clean;
        }
        if(value.TryGetProperty("factory",out var factory)&&factory.ValueKind==JsonValueKind.Object)
        {
            var clean=new JsonObject{["found"]=factory.TryGetProperty("found",out var found)&&found.ValueKind==JsonValueKind.True};
            foreach(var name in new[]{"helperSource","retrySource"})
                if(factory.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String&&field.GetString() is {Length:<=4000} text&&
                    !text.Contains("https:",StringComparison.OrdinalIgnoreCase)&&!text.Contains("http:",StringComparison.OrdinalIgnoreCase))clean[name]=text;
            output["factory"]=clean;
        }
        if(value.TryGetProperty("candidates",out var candidates)&&candidates.ValueKind==JsonValueKind.Array)
        {
            var clean=new JsonArray();
            foreach(var item in candidates.EnumerateArray().Take(8))
            {
                if(item.ValueKind!=JsonValueKind.Object)continue;
                var candidate=new JsonObject();
                foreach(var name in new[]{"selected","hasDel","hasDelete"})
                    candidate[name]=item.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.True;
                foreach(var name in new[]{"index","getArity","postArity"})
                    if(item.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.Number&&field.TryGetInt32(out var count)&&count is >=0 and <=10)candidate[name]=count;
                foreach(var name in new[]{"getSource","postSource"})
                    if(item.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String&&field.GetString() is {Length:<=4000} text &&
                        !text.Contains("https:",StringComparison.OrdinalIgnoreCase)&&!text.Contains("http:",StringComparison.OrdinalIgnoreCase))candidate[name]=text;
                var fields=new JsonArray();
                if(item.TryGetProperty("fields",out var names)&&names.ValueKind==JsonValueKind.Array)
                    foreach(var name in names.EnumerateArray().Take(32))
                        if(name.ValueKind==JsonValueKind.String&&name.GetString() is {Length:>0 and <=64} text&&text.All(c=>char.IsAsciiLetter(c)||c is '_' or '$'))fields.Add(text);
                candidate["fields"]=fields;clean.Add(candidate);
            }
            output["candidates"]=clean;
        }
        return output;
    }

    private static readonly string[] DiscordMessagingShapeFields=["hasBody","bodyArray","count","overLimit","idValid","channelPresent","channelMatches",
        "authorValid","contentString","contentNull","contentMissing","attachmentsArray","timestampValid","editedValid","attachmentIdValid",
        "attachmentNameString","attachmentSizeInteger","duplicateIds","httpSuccess","requestLimitInteger","attemptSeen","rejected","httpStatus",
        "channelKnown","channelIdMatches","privateType","guildAbsent","recipientsArray","recipientCount","recipientsValid","recipientIncludesSelf","recipientsUnique","dmRecipientCount",
        "errorBodyObject","unknownChannelCode","unknownEndpointCode","missingAccessCode",
        "klipyEmbeds","tenorEmbeds","gifvEmbeds","imageEmbeds","videoEmbeds","animatedEmbedImages",
        "embedVideoDiscordProxy","embedImageDiscordProxy","embedVideoTenor","embedVideoOther",
        "videoAttachments","attachmentProxyPresent","attachmentThumbnailPresent","animatedAttachments"];
    private const string DiscordMessagingProbeScript="""
        (async()=>{
          const adapter=globalThis.WidgetRailDiscordPrototype;
          if(!adapter?.invoke)return {available:false};
          const wp=globalThis.WidgetRailWebWebpack;
          const api=wp?.findByProps?.('get','post','del','put');
          const authenticated=wp?.findByProps?.('getAPIBaseURL');
          const facade=wp?.findByProps?.('get','post','delete','patch');
          const actions=wp?.findByProps?.('editMessage','sendMessage');
          const key='__widgetRailMessagingShapeProbe';
          globalThis[key]??={shape:null,registrations:[]};
          const diagnostic=globalThis[key];
          // Count only fixed provider facts from responses the widget already
          // requested. No media fetches, message text, identities, filenames,
          // signed URLs or arbitrary hostnames leave the page through this probe.
          const mediaFacts=rows=>{
            const host=value=>{try{return new URL(value).hostname;}catch{return '';}};
            const proxy=value=>['images-ext-1.discordapp.net','images-ext-2.discordapp.net'].includes(host(value));
            const animated=value=>Number.isSafeInteger(value?.flags)&&value.flags>=0&&(value.flags&32)!==0;
            const embeds=rows.flatMap(row=>Array.isArray(row?.embeds)?row.embeds.slice(0,2):[]).slice(0,100);
            const attachments=rows.flatMap(row=>Array.isArray(row?.attachments)?row.attachments.slice(0,10):[]).slice(0,100);
            const image=entry=>entry?.image??entry?.thumbnail;
            const address=value=>value?.proxy_url??value?.proxyURL??value?.url;
            return {
              klipyEmbeds:embeds.filter(value=>host(value?.url)==='klipy.com').length,
              tenorEmbeds:embeds.filter(value=>['tenor.com','www.tenor.com'].includes(host(value?.url))).length,
              gifvEmbeds:embeds.filter(value=>value?.type==='gifv').length,
              imageEmbeds:embeds.filter(value=>value?.type==='image').length,
              videoEmbeds:embeds.filter(value=>value?.type==='video').length,
              animatedEmbedImages:embeds.filter(value=>animated(image(value))).length,
              embedVideoDiscordProxy:embeds.filter(value=>proxy(address(value?.video))).length,
              embedImageDiscordProxy:embeds.filter(value=>proxy(address(image(value)))).length,
              embedVideoTenor:embeds.filter(value=>host(address(value?.video))==='media.tenor.com').length,
              embedVideoOther:embeds.filter(value=>{const name=host(address(value?.video));return name&&!proxy(address(value.video))&&!['media.tenor.com','cdn.discordapp.com','media.discordapp.net'].includes(name);}).length,
              videoAttachments:attachments.filter(value=>typeof value?.content_type==='string'&&value.content_type.startsWith('video/')).length,
              attachmentProxyPresent:attachments.filter(value=>typeof value?.proxy_url==='string').length,
              attachmentThumbnailPresent:attachments.filter(value=>value?.thumbnail!=null||value?.poster!=null).length,
              animatedAttachments:attachments.filter(animated).length
            };
          };
          if(!diagnostic.findOriginal&&typeof wp?.findByProps==='function'){
            diagnostic.findOriginal=wp.findByProps;
            const wrapper=function(...args){
              const result=Reflect.apply(diagnostic.findOriginal,this,args);
              if(!diagnostic.enumerating&&args.length===3&&args[0]==='get'&&args[1]==='post'&&typeof args[2]==='function')diagnostic.selected=result;
              return result;
            };
            diagnostic.findWrapper=wrapper;wp.findByProps=wrapper;
            setTimeout(()=>{if(wp.findByProps===wrapper)wp.findByProps=diagnostic.findOriginal;},60000);
          }
          const originalGet=target=>globalThis[key]?.registrations.find(item=>item.target===target)?.original??target?.get;
          const scrubSource=source=>{
            if(source.length>200000)return 'oversized';
            const quote=index=>{const delimiter=source[index++];while(index<source.length){if(source[index]==='\\'){index+=2;continue;}if(source[index++]===delimiter)return index;}return index;};
            const comment=index=>{const line=source[index+1]==='/',end=source.indexOf(line?'\n':'*/',index+2);return end<0?source.length:end+(line?1:2);};
            const expression=index=>{
              let depth=1;
              while(index<source.length){const value=source[index];
                if(value==='"'||value==="'"){index=quote(index);continue;}
                if(value==='`'){index=template(index);continue;}
                if(value==='/'&&(source[index+1]==='/'||source[index+1]==='*')){index=comment(index);continue;}
                // Complex slash/regex expressions inside a template are omitted
                // conservatively; never expose literal text by guessing its end.
                if(value==='/')return source.length;
                if(value==='{')depth++;else if(value==='}'&&--depth===0)return index+1;
                index++;
              }return index;
            };
            const template=index=>{
              index++;while(index<source.length){if(source[index]==='\\'){index+=2;continue;}
                if(source[index]==='`')return index+1;
                if(source[index]==='$'&&source[index+1]==='{'){index=expression(index+2);continue;}index++;
              }return index;
            };
            let output='',index=0;
            while(index<source.length){const value=source[index];
              if(value==='"'||value==="'"||value==='`'){index=value==='`'?template(index):quote(index);output+='""';continue;}
              if(value==='/'&&(source[index+1]==='/'||source[index+1]==='*')){index=comment(index);output+=' ';continue;}
              output+=value;index++;
            }return output.replace(/\b[0-9]{8,}\b/g,'0');
          };
          const scrubFull=fn=>typeof fn==='function'?scrubSource(Function.prototype.toString.call(fn)):'';
          const scrub=fn=>scrubFull(fn).slice(0,4000);
          const fetchSource=scrubFull(actions?.fetchMessages),getAt=fetchSource.indexOf('.get(');
          const sources={genericGet:scrub(originalGet(api)),facadeGet:scrub(originalGet(facade)),messageFetch:fetchSource.slice(0,4000),
            messageFetchRequest:getAt<0?fetchSource.slice(-4000):fetchSource.slice(Math.max(0,getAt-300),getAt+3700)};
          const describe=(candidate,index)=>({target:candidate,index,getArity:originalGet(candidate).length,postArity:candidate.post.length,
            hasDel:typeof candidate.del==='function',hasDelete:typeof candidate.delete==='function',
            fields:Object.getOwnPropertyNames(candidate).filter(name=>/^[A-Za-z_$]{1,64}$/.test(name)).slice(0,32),
            getSource:scrub(originalGet(candidate)),postSource:scrub(candidate.post)});
          if(!diagnostic.candidates){
            const seen=new Set(),candidates=[];
            const matches=value=>value&&typeof value.get==='function'&&typeof value.post==='function'&&
              (typeof value.del==='function'||typeof value.delete==='function')&&!seen.has(value);
            diagnostic.enumerating=true;
            try{
              for(let index=0;index<8;index++){
                const candidate=wp?.findByProps?.('get','post',matches);if(!candidate||seen.has(candidate))break;seen.add(candidate);
                candidates.push(describe(candidate,index));
              }
            }finally{diagnostic.enumerating=false;}
            if(candidates.length)diagnostic.candidates=candidates;
          }
          if(diagnostic.selected&&!diagnostic.candidates?.some(item=>item.target===diagnostic.selected)){
            const retained=(diagnostic.candidates??[]).slice(0,7);
            diagnostic.candidates=[...retained,describe(diagnostic.selected,retained.length)];
          }
          if(diagnostic.selected&&diagnostic.factoryTarget!==diagnostic.selected){
            diagnostic.factoryTarget=diagnostic.selected;diagnostic.factory={found:false};
            const runtimes=new Set(),chunks=globalThis.webpackChunkdiscord_app;
            if(Array.isArray(chunks)){
              chunks.push([['widgetrail-source-probe-'+crypto.randomUUID()],{},runtime=>runtimes.add(runtime)]);chunks.pop();
              let found=false;
              for(const runtime of runtimes){
                if(found)break;
                for(const [moduleId,module]of Object.entries(runtime.c??{}).slice(0,30000)){
                  const exports=module?.exports;if(!exports||!['object','function'].includes(typeof exports))continue;
                  let matches=exports===diagnostic.selected;
                  if(!matches)for(const name of Object.getOwnPropertyNames(exports).filter(name=>/^[A-Za-z_$]{1,4}$|^(?:default|HTTP|RestAPI)$/.test(name)&&
                    !['auth','key','id','user','token','secret','pass','pwd','data'].includes(name.toLowerCase())).slice(0,32)){
                    try{if(exports[name]===diagnostic.selected){matches=true;break;}}catch{}
                  }
                  if(!matches||typeof runtime.m?.[moduleId]!=='function')continue;
                  const body=scrubFull(runtime.m[moduleId]),get=scrub(originalGet(diagnostic.selected));
                  const helper=/return\s+([A-Za-z_$][A-Za-z0-9_$]*)\(/.exec(get)?.[1];
                  const declaration=helper?(new RegExp('(?:async\\s+)?function\\s+'+helper+'\\s*\\(').exec(body)??
                    new RegExp('\\b'+helper+'\\s*=(?!=)').exec(body)):null;
                  const retry=body.indexOf('retries');
                  diagnostic.factory={found:true,helperSource:declaration?body.slice(declaration.index,declaration.index+4000):'',
                    retrySource:retry<0?'':body.slice(Math.max(0,retry-500),retry+3500)};found=true;break;
                }
              }
            }
          }
          const apiFacts={authenticatedModuleFound:!!authenticated,sameAsGeneric:authenticated===api,authenticatedGet:typeof authenticated?.get==='function',
            authenticatedPost:typeof authenticated?.post==='function',authenticatedDelete:typeof authenticated?.delete==='function',authenticatedDel:typeof authenticated?.del==='function',
            genericBaseGetter:typeof api?.getAPIBaseURL==='function',apiEnvPresent:typeof globalThis.GLOBAL_ENV?.API_ENDPOINT==='string',
            deleteFacadeFound:!!facade,deleteFacadeSameGeneric:facade===api,deleteFacadeGet:typeof facade?.get==='function',deleteFacadePost:typeof facade?.post==='function',
            genericGetArity:originalGet(api)?.length,genericPostArity:api?.post?.length,facadeGetArity:originalGet(facade)?.length,facadePostArity:facade?.post?.length,
            fetchMessagesFound:typeof actions?.fetchMessages==='function',fetchUsesObjectGet:/\.get\(\{/.test(fetchSource)};
          try{
            const base=authenticated?.getAPIBaseURL?.();
            if(typeof base==='string'){
              const url=new URL(base,location.href);apiFacts.authenticatedBaseSameOrigin=url.origin===location.origin;
              apiFacts.authenticatedBaseApiPath=/^\/api(?:\/v[0-9]+)?\/?$/.test(url.pathname);
              apiFacts.authenticatedBaseVersioned=/^\/api\/v[0-9]+\/?$/.test(url.pathname);
            }
          }catch{}
          try{
            const endpoints=wp?.findByProps?.('USER','ME'),endpoint=typeof endpoints?.MESSAGES==='function'?endpoints.MESSAGES('1'):null;
            apiFacts.endpointMessagesFound=typeof endpoint==='string';apiFacts.endpointRelative=endpoint==='/channels/1/messages';
            apiFacts.endpointVersioned=typeof endpoint==='string'&&/^\/api\/v[0-9]+\/channels\/1\/messages$/.test(endpoint);
            apiFacts.endpointAbsolute=typeof endpoint==='string'&&/^https:\/\/discord\.com\/api\//.test(endpoint);
          }catch{}
          try{
            const entries=performance.getEntriesByType('resource').slice(-100),recent=entries.filter(entry=>/\/(?:api\/v[0-9]+\/)?channels\/[0-9]+\/messages(?:\?|$)/.test(entry.name)).at(-1);
            apiFacts.objectObjectRequest=entries.some(entry=>/\/\[object(?:%20| )Object\](?:\?|$)|\/%5Bobject%20Object%5D(?:\?|$)/i.test(entry.name));
            if(recent){const url=new URL(recent.name,location.href);apiFacts.recentHistorySitePath=/^\/channels\/[0-9]+\/messages$/.test(url.pathname);
              apiFacts.recentHistoryApiPath=/^\/api(?:\/v[0-9]+)?\/channels\/[0-9]+\/messages$/.test(url.pathname);apiFacts.recentHistorySameOrigin=url.origin===location.origin;}
          }catch{}
          for(const target of new Set([api,authenticated,facade,...(diagnostic.candidates??[]).map(item=>item.target)]))if(target&&typeof target.get==='function'&&!globalThis[key].registrations.some(item=>item.target===target)){
            const probe=globalThis[key],registration={target,original:target.get};
            const validId=value=>typeof value==='string'&&/^[1-9][0-9]{0,31}$/.test(value);
            const wrapper=function(...args){
              const request=args[0],match=typeof request?.url==='string'?/^\/channels\/([0-9]+)\/messages$/.exec(request.url):null;
              let entry={attemptSeen:true};
              if(match){
                try{
                  const channel=wp?.findStore?.('ChannelStore')?.getChannel?.(match[1]),self=wp?.findStore?.('UserStore')?.getCurrentUser?.()?.id;
                  const recipients=channel?.recipients;
                  entry={attemptSeen:true,rejected:false,channelKnown:!!channel,channelIdMatches:channel?.id===match[1],privateType:[1,3].includes(channel?.type),
                    guildAbsent:channel?.guild_id==null,recipientsArray:Array.isArray(recipients),recipientCount:Array.isArray(recipients)?Math.min(recipients.length,100):0,
                    recipientsValid:Array.isArray(recipients)&&recipients.every(validId),recipientIncludesSelf:Array.isArray(recipients)&&recipients.includes(self),
                    recipientsUnique:Array.isArray(recipients)&&new Set(recipients).size===recipients.length,dmRecipientCount:channel?.type!==1||recipients?.length===1};
                }catch{}
                probe.shape=entry;
              }
              const rejected=error=>{
                if(match){
                  let facts={};
                  try{const status=error?.status??error?.response?.status,body=error?.body??error?.response?.body;
                    facts={httpStatus:Number.isInteger(status)&&status>=0&&status<=599?status:0,errorBodyObject:body!==null&&typeof body==='object',
                      unknownChannelCode:body?.code===10003,unknownEndpointCode:body?.code===0,missingAccessCode:body?.code===50001};}catch{}
                  probe.shape={...entry,rejected:true,...facts};
                }
                throw error;
              };
              let pending;
              try{pending=Reflect.apply(registration.original,this,args);}catch(error){return rejected(error);}
              if(!match)return pending;
              return Promise.resolve(pending).then(response=>{
                try{
                  const rows=Array.isArray(response?.body)?response.body.slice(0,50):[];
                  const count=predicate=>rows.filter(predicate).length;
                  const attachments=rows.flatMap(row=>Array.isArray(row?.attachments)?row.attachments.slice(0,10):[]).slice(0,100);
                  probe.shape={...entry,httpStatus:Number.isInteger(response?.status)&&response.status>=0&&response.status<=599?response.status:0,
                    hasBody:response!=null&&Object.hasOwn(response,'body'),bodyArray:Array.isArray(response?.body),count:rows.length,
                    overLimit:Array.isArray(response?.body)&&response.body.length>50,httpSuccess:response?.status>=200&&response?.status<300,
                    requestLimitInteger:Number.isInteger(request?.query?.limit),idValid:count(row=>validId(row?.id)),
                    channelPresent:count(row=>Object.hasOwn(row??{},'channel_id')),channelMatches:count(row=>row?.channel_id===match[1]),
                    authorValid:count(row=>validId(row?.author?.id)),contentString:count(row=>typeof row?.content==='string'),
                    contentNull:count(row=>row?.content===null),contentMissing:count(row=>row?.content===undefined),
                    attachmentsArray:count(row=>Array.isArray(row?.attachments)),timestampValid:count(row=>typeof row?.timestamp==='string'&&Number.isFinite(Date.parse(row.timestamp))),
                    editedValid:count(row=>row?.edited_timestamp==null||typeof row.edited_timestamp==='string'&&Number.isFinite(Date.parse(row.edited_timestamp))),
                    attachmentIdValid:attachments.every(item=>validId(item?.id)),attachmentNameString:attachments.every(item=>typeof item?.filename==='string'),
                    attachmentSizeInteger:attachments.every(item=>Number.isSafeInteger(item?.size)&&item.size>=0),duplicateIds:new Set(rows.map(row=>row?.id)).size!==rows.length,
                    ...mediaFacts(rows)};
                }catch{probe.shape={...entry,hasBody:false};}
                return response;
              },rejected);
            };
            registration.wrapper=wrapper;probe.registrations.push(registration);target.get=wrapper;
            setTimeout(()=>{if(target.get===wrapper)target.get=registration.original;},60000);
          }
          const response=await adapter.invoke({method:'diagnostics',owner:'00000000000000000000000000000000'});
          const failure=response?.value??{},methods=['messageHistory','messageConversations','messageObserve','messageOpenDm','messageSend','messageReconcile','messagePrepareAttachments'];
          const types=['Error','TypeError','RangeError','SyntaxError'];
          const codes=['account_changed','messaging_retired','messaging_unavailable','invalid_conversation','conversation_unavailable','invalid_page','invalid_message',
            'invalid_message_response','rate_limited','message_forbidden','message_rejected','message_unconfirmed','message_updates_unavailable'];
          const shape=globalThis[key]?.shape;
          return {available:true,restFound:!!api,probeAttached:globalThis[key]?.registrations.some(item=>item.target.get===item.wrapper)===true,
            hasFailure:!!failure.method,method:!failure.method?'none':methods.includes(failure.method)?failure.method:'other',
            type:!failure.type?'none':types.includes(failure.type)?failure.type:'other',code:!failure.method?'none':codes.includes(failure.detail)?failure.detail:'unclassified',
            shapeObserved:!!shape,shape:shape??null,apiFacts,sources,
            candidates:(diagnostic.candidates??[]).map(({target,...info})=>({...info,selected:target===diagnostic.selected})),factory:diagnostic.factory??{found:false}};
        })()
        """;
}
