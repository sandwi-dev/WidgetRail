// Discord-specific data/commands over the authenticated client already hosted by WidgetRail.
// No credentials, browser navigation, independent transport or upload buffers cross this module.
// Internal module discovery is feature-probed; endpoint shapes follow Discord's Message/User resources.
globalThis.createWidgetRailDiscordMessaging = ({store,props,me,dispatcher=()=>null,invalidate=()=>{}}) => {
    const PAGE_SIZE=50, CATALOG_LIMIT=1000, SEND_LIMIT=2000, READ_TEXT_LIMIT=4000, LEDGER_LIMIT=128;
    const id=value=>typeof value==='string'&&/^[1-9][0-9]{0,31}$/.test(value);
    const nonce=value=>typeof value==='string'&&/^[0-9]{1,25}$/.test(value);
    const fail=code=>{throw Object.assign(new Error(code),{adapterCode:code});};
    const ledger=new Map();
    const prepared=new Map();
    const openingChannels=new Map();
    let pendingPreparations=0;
    let disposed=false, catalogVersion=0, catalogFingerprint='';
    let observed=null, attachedDispatcher=null, messageRevision=0, catalogRevision=0, readState=null, readFingerprint='';
    const knownChannels=new Set();
    // One watched conversation, bounded independently of its lifetime. Reads
    // acknowledge by revision rather than consuming events, so retries are safe.
    const changes=[], messageCache=new Map();
    const CHANGE_LIMIT=128, CACHE_LIMIT=200;
    let watchEpoch=0, changeFloor=0;
    const messageEvents=['MESSAGE_CREATE','MESSAGE_UPDATE','MESSAGE_DELETE','MESSAGE_DELETE_BULK','CHANNEL_CREATE','CHANNEL_UPDATE','CHANNEL_DELETE',
        'MESSAGE_REACTION_ADD','MESSAGE_REACTION_REMOVE','MESSAGE_REACTION_REMOVE_ALL','MESSAGE_REACTION_REMOVE_EMOJI','MESSAGE_POLL_VOTE_ADD','MESSAGE_POLL_VOTE_REMOVE'];
    const subscriptions=new Map(messageEvents.map(name=>[name,event=>changed(event,name)]));
    const notificationMessages=new Map();
    let notificationDispatcher=null,notificationAccount=null;
    // Discord's current NotificationStore uses the MESSAGE_CREATE message ID
    // as the native Notification.tag. Preserve only this exact association;
    // no title matching, extra HTTP, notification creation or read receipts.
    function notificationMessage(event){
        const accountId=me()?.id,raw=event?.message;
        if(disposed||accountId!==notificationAccount||!id(raw?.id)||!id(raw.channel_id))return;
        try{
            const channel=privateChannel(accountId,raw.channel_id);
            if(raw.author?.id===accountId)return;
            notificationMessages.delete(raw.id);notificationMessages.set(raw.id,{account:accountId,channelId:channel.id});
            if(notificationMessages.size>256)notificationMessages.delete(notificationMessages.keys().next().value);
        }catch{}
    }
    function detachNotificationObserver(){
        try{notificationDispatcher?.unsubscribe('MESSAGE_CREATE',notificationMessage);}catch{}
        notificationDispatcher=null;notificationAccount=null;notificationMessages.clear();
    }
    function syncNotificationObserver(){
        const current=me()?.id,source=dispatcher();
        if(notificationDispatcher===source&&notificationAccount===current)return;
        detachNotificationObserver();
        if(disposed||!id(current)||typeof source?.subscribe!=='function'||typeof source?.unsubscribe!=='function')return;
        try{notificationDispatcher=source;notificationAccount=current;source.subscribe('MESSAGE_CREATE',notificationMessage);}
        catch{detachNotificationObserver();}
    }
    function notificationTarget(args){
        account(args?.account);syncNotificationObserver();
        if(!id(args?.tag))return null;
        const found=notificationMessages.get(args.tag);
        if(!found||found.account!==args.account)return null;
        try{
            const channel=privateChannel(args.account,found.channelId);
            return {account:args.account,messageId:args.tag,conversation:conversation(channel,args.account)};
        }catch{return null;}
    }
    const fileName=value=>typeof value==='string'&&value.length>0&&value.length<=256&&value!=='.'&&value!=='..'&&
        !/[\u0000-\u001f\u007f-\u009f/\\]/.test(value)&&text(value,256)===value;
    const fileId=value=>typeof value==='string'&&/^[0-9]$/.test(value);
    function uploadLimit(){
        try{
            const read=props('getUserMaxFileSize')?.getUserMaxFileSize;
            const value=typeof read==='function'?read(me()):null;
            return Number.isSafeInteger(value)&&value>0&&value<=2147483648?value:null;
        }catch{return null;}
    }
    function detachObserver(){
        if(attachedDispatcher)for(const[name,callback]of subscriptions)try{attachedDispatcher.unsubscribe(name,callback);}catch{}
        if(readState)try{readState.removeChangeListener(readChanged);}catch{}
        readState=null;readFingerprint='';knownChannels.clear();
        attachedDispatcher=null;
    }
    function readSignature(){
        return JSON.stringify([...knownChannels].map(key=>[key,readState?.lastMessageId?.(key),readState?.hasUnread?.(key),oldestUnread(readState,key)]));
    }
    function oldestUnread(reads,channel){
        if(reads?.hasUnread?.(channel)!==true)return null;
        try{const value=reads?.getOldestUnreadMessageId?.(channel);return id(value)?value:null;}catch{return null;}
    }
    function readChanged(){
        if(disposed||!observed||me()?.id!==observed.account)return;
        const next=readSignature();if(next===readFingerprint)return;
        readFingerprint=next;++catalogRevision;invalidate();
    }
    function changed(event,name){
        if(disposed||!observed||me()?.id!==observed.account)return;
        const channel=event?.message?.channel_id??event?.message?.channelId??event?.channel?.id??event?.channelId??event?.channel_id;
        const record=event?.channel??store('ChannelStore')?.getChannel?.(channel);
        const isPrivate=id(channel)&&(knownChannels.has(channel)||record?.guild_id==null&&[1,3].includes(record?.type));
        let dirty=false;
        if(isPrivate&&(name==='MESSAGE_CREATE'||name.startsWith('CHANNEL_'))){
            if(name==='CHANNEL_DELETE')knownChannels.delete(channel);
            else{
                if(knownChannels.size>=CATALOG_LIMIT&&!knownChannels.has(channel))knownChannels.delete(knownChannels.values().next().value);
                knownChannels.add(channel);
            }
            ++catalogRevision;dirty=true;
        }
        if(channel===observed.channelId&&name.startsWith('MESSAGE_')){
            if(name.startsWith('MESSAGE_REACTION_')||name.startsWith('MESSAGE_POLL_')){refreshMessageExtras(event,name);return;}
            recordChanges(event,name);dirty=true;
        }
        if(dirty)invalidate();
    }
    function observe(args){
        account(args?.account);
        if(args.active!==false&&args.channelId!=null)privateChannel(args.account,args.channelId);
        const next=args.active===false?null:{account:args.account,channelId:args.channelId??null,sinceRevision:messageRevision};
        const current=next?dispatcher():null;
        if(next&&(typeof current?.subscribe!=='function'||typeof current?.unsubscribe!=='function'))fail('message_updates_unavailable');
        if(observed?.account===next?.account&&observed?.channelId===next?.channelId&&current===attachedDispatcher)return state();
        detachObserver();observed=next;++catalogRevision;
        ++watchEpoch;changes.length=0;messageCache.clear();changeFloor=messageRevision;
        if(next){
            attachedDispatcher=current;
            try{
                for(const[name,callback]of subscriptions)current.subscribe(name,callback);
                const channels=store('ChannelStore')?.getSortedPrivateChannels?.();
                if(Array.isArray(channels))for(const channel of channels.slice(0,CATALOG_LIMIT))
                    if(id(channel?.id)&&channel.guild_id==null&&[1,3].includes(channel.type))knownChannels.add(channel.id);
                const reads=store('ReadStateStore');
                if(typeof reads?.addChangeListener==='function'&&typeof reads?.removeChangeListener==='function'){
                    readState=reads;readFingerprint=readSignature();reads.addChangeListener(readChanged);
                }
            }
            catch{detachObserver();observed=null;fail('message_updates_unavailable');}
        }
        return state();
    }
    function text(value,limit,multiline=false){
        if(typeof value!=='string')return '';
        let result='';
        for(const scalar of value){
            if(result.length+scalar.length>limit)break;
            if(/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/.test(scalar))continue;
            if(!multiline&&(scalar==='\n'||scalar==='\r'||scalar==='\t')){result+=' ';continue;}
            result+=scalar.length===1&&/[\ud800-\udfff]/.test(scalar)?'\ufffd':scalar;
        }
        return result;
    }
    function avatar(user){
        if(!id(user?.id))return null;
        if(typeof user.avatar==='string'&&/^(?:a_)?[a-f0-9]{32}$/.test(user.avatar))
            return 'https://cdn.discordapp.com/avatars/'+user.id+'/'+user.avatar+'.png?size=64';
        return null;
    }
    function account(expected){
        if(disposed)fail('messaging_retired');
        const current=me()?.id;
        if(!id(expected)||current!==expected)fail('account_changed');
        return current;
    }
    function rest(){
        // Discord's message actions use the object verb bag ending in `del`.
        // The callable superagent export has positional methods; the separate
        // `delete` tracking wrapper requires trackedActionData and can leave its
        // outer promise unsettled without it. Function arity does not distinguish
        // the authenticated API: its bound methods currently accept two args.
        const compatible=api=>api&&typeof api==='object'&&!Array.isArray(api)&&
            ['get','post','put','patch','del'].every(name=>typeof api[name]==='function')&&
            !['Request','Response','agent'].some(name=>typeof api[name]==='function');
        const api=props('get','post',compatible);
        return compatible(api)?api:null;
    }
    function available(){
        const channels=store('ChannelStore');
        return typeof channels?.getChannel==='function'&&typeof channels?.getSortedPrivateChannels==='function'&&rest()!==null;
    }
    function relationships(){
        const value=store('RelationshipStore');
        return typeof value?.isBlockedOrIgnored==='function'?value:null;
    }
    function state(){
        syncNotificationObserver();
        const current=me()?.id;
        if(observed&&current!==observed.account){detachObserver();observed=null;++catalogRevision;++watchEpoch;changes.length=0;messageCache.clear();changeFloor=messageRevision;}
        const connected=!disposed&&id(current), ready=connected&&available();
        return {account:connected?current:null,availability:!connected?'disconnected':ready?'ready':'unavailable',
            canRead:ready,canSend:ready&&relationships()!==null,
            canOpenDm:ready&&typeof relationships()?.isFriend==='function',maximumContentLength:SEND_LIMIT,maximumPageSize:PAGE_SIZE,
            maximumAttachmentBytes:connected?uploadLimit():null,messageRevision,catalogRevision,observedChannelId:observed?.channelId??null,
            observedSinceRevision:observed?.sinceRevision??messageRevision,watchEpoch};
    }
    function privateChannel(expected,channelId,send=false){
        account(expected);
        if(!id(channelId))fail('invalid_conversation');
        let value=store('ChannelStore')?.getChannel?.(channelId);
        const opening=openingChannels.get(channelId);
        if(value)openingChannels.delete(channelId);
        else if(opening?.account===expected&&opening.expiresAt>Date.now())value=opening.channel;
        if(!value||value.id!==channelId||value.guild_id!=null||![1,3].includes(value.type)||
            !Array.isArray(value.recipients)||value.recipients.length<1||value.recipients.length>10||
            value.recipients.some(user=>!id(user)||user===expected)||new Set(value.recipients).size!==value.recipients.length||
            value.type===1&&value.recipients.length!==1)fail('conversation_unavailable');
        if(send){
            const relations=relationships();
            if(!relations)fail('messaging_unavailable');
            if(value.recipients.some(user=>relations.isBlockedOrIgnored(user)!==false))fail('recipient_unavailable');
        }
        account(expected);
        return value;
    }
    function conversation(value,expected){
        const users=store('UserStore');
        const recipient=value.type===1?value.recipients[0]:null;
        const user=recipient?users?.getUser?.(recipient):null;
        const names=value.recipients.map(key=>text(users?.getUser?.(key)?.globalName||users?.getUser?.(key)?.username,120)).filter(Boolean);
        let canSend=false;
        try{privateChannel(expected,value.id,true);canSend=true;}catch{}
        const reads=store('ReadStateStore');
        const last=reads?.lastMessageId?.(value.id)??value.lastMessageId??value.last_message_id;
        const unread=reads?.hasUnread?.(value.id)===true;
        // Discord's read store supplies the oldest known unread identity. An
        // unread boolean alone cannot identify a boundary; never synthesize one.
        const unreadTarget=oldestUnread(reads,value.id);
        return {id:value.id,channelId:value.id,userId:recipient,kind:value.type===1?'dm':'group',
            name:text(value.type===1?(user?.globalName||user?.username):value.name||names.join(', '),120)||'Direct message',
            avatarUrl:avatar(user),lastMessageId:id(last)?last:null,
            unread,oldestUnreadMessageId:unreadTarget,canSend};
    }
    function pageLimit(value){if(value===undefined)return PAGE_SIZE;if(!Number.isSafeInteger(value)||value<1||value>PAGE_SIZE)fail('invalid_page');return value;}
    function conversations(args){
        account(args?.account);
        if(!available())fail('messaging_unavailable');
        const limit=pageLimit(args.limit), offset=args.offset??0, query=args.query??'';
        if(!Number.isSafeInteger(offset)||offset<0||offset>CATALOG_LIMIT||typeof query!=='string'||query.length>120)fail('invalid_page');
        const channels=store('ChannelStore').getSortedPrivateChannels();
        if(!Array.isArray(channels))fail('messaging_unavailable');
        const rows=[], seen=new Set(), recipients=new Set();
        let truncated=channels.length>CATALOG_LIMIT;
        for(const value of channels.slice(0,CATALOG_LIMIT)){
            if(!id(value?.id)||seen.has(value.id))continue;
            try{
                const current=privateChannel(args.account,value.id);
                rows.push(conversation(current,args.account));seen.add(current.id);
                if(current.type===1)recipients.add(current.recipients[0]);
            }catch(error){if(error.adapterCode==='account_changed'||error.adapterCode==='messaging_retired')throw error;}
        }
        const relations=relationships(), users=store('UserStore');
        const friends=typeof relations?.getFriendIDs==='function'?relations.getFriendIDs():[];
        if(Array.isArray(friends)){
            truncated||=friends.length>CATALOG_LIMIT;
            const additions=[];
            for(const key of friends.slice(0,CATALOG_LIMIT)){
                if(!id(key)||key===args.account||recipients.has(key)||relations.isFriend?.(key)!==true||relations.isBlockedOrIgnored(key)!==false)continue;
                const user=users?.getUser?.(key);if(user?.id!==key)continue;
                recipients.add(key);
                additions.push({id:'friend.'+key,channelId:null,userId:key,kind:'friend',name:text(user.globalName||user.username,120)||'Friend',
                    avatarUrl:avatar(user),lastMessageId:null,unread:false,canSend:true});
            }
            additions.sort((a,b)=>a.name.localeCompare(b.name)||a.id.localeCompare(b.id));
            truncated||=rows.length+additions.length>CATALOG_LIMIT;
            rows.push(...additions.slice(0,CATALOG_LIMIT-rows.length));
        }
        account(args.account);
        const fingerprint=JSON.stringify([args.account,rows]);
        if(fingerprint!==catalogFingerprint){catalogFingerprint=fingerprint;++catalogVersion;}
        const version='catalog-'+catalogVersion;
        if(args.version!=null&&args.version!==version)fail('conversation_catalog_changed');
        const needle=query.trim().toLocaleLowerCase(), matches=needle?rows.filter(row=>row.name.toLocaleLowerCase().includes(needle)):rows;
        const items=matches.slice(offset,offset+limit);
        return {account:args.account,version,totalCount:matches.length,offset,hasMore:offset+items.length<matches.length,truncated,items};
    }
    function attachment(value){
        if(!id(value?.id)||typeof value.filename!=='string'||!Number.isSafeInteger(value.size)||value.size<0)fail('invalid_message');
        let url=null;
        try{
            const parsed=new URL(value.url);
            if(parsed.protocol==='https:'&&!parsed.username&&!parsed.password&&!parsed.hash&&(!parsed.port||parsed.port==='443')&&
                ['cdn.discordapp.com','media.discordapp.net'].includes(parsed.hostname)&&
                parsed.pathname.startsWith('/attachments/')&&parsed.href.length<=2048)url=parsed.href;
        }catch{}
        const dimension=value=>Number.isSafeInteger(value)&&value>0&&value<=65535?value:null;
        return {id:value.id,fileName:text(value.filename,256)||'Attachment',contentType:typeof value.content_type==='string'?text(value.content_type,120):null,
            byteLength:value.size,url,width:dimension(value.width),height:dimension(value.height)};
    }
    function mentions(value){
        if(!Array.isArray(value))return [];
        const seen=new Set(),items=[];
        for(const user of value.slice(0,64)){
            if(!id(user?.id)||seen.has(user.id))continue;
            seen.add(user.id);items.push({id:user.id,name:text(user.global_name||user.globalName||user.username,120)||'User'});
            if(items.length===32)break;
        }
        return items;
    }
    function webUrl(value,media=false){
        if(typeof value!=='string'||value.length>2048||/[\u0000-\u001f\u007f-\u009f]/.test(value))return null;
        try{
            const url=new URL(value);
            if(!['https:','http:'].includes(url.protocol)||url.username||url.password)return null;
            if(media&&(url.protocol!=='https:'||url.port&&url.port!=='443'||url.hash||
                !['cdn.discordapp.com','media.discordapp.net'].includes(url.hostname)))return null;
            return url.href;
        }catch{return null;}
    }
    function reply(value){
        const key=value.message_reference?.message_id;
        if(!id(key))return null;
        const source=value.referenced_message;
        return {id:key,author:text(source?.author?.global_name||source?.author?.globalName||source?.author?.username,120)||'Original message',
            content:typeof source?.content==='string'?text(source.content,240,true):'',unavailable:source==null};
    }
    function embeds(value){
        if(!Array.isArray(value))return [];
        return value.slice(0,2).filter(item=>item&&typeof item==='object').map(item=>({
            title:text(item.title||item.author?.name||item.provider?.name,256),description:text(item.description,600,true),url:webUrl(item.url),
            // Only Discord's image/proxy domains are fetched by the native view.
            imageUrl:webUrl(item.image?.proxy_url??item.thumbnail?.proxy_url??item.image?.url??item.thumbnail?.url,true)}));
    }
    function reactions(value){
        if(!Array.isArray(value))return [];
        return value.slice(0,12).filter(item=>Number.isSafeInteger(item?.count)&&item.count>0&&item.count<=1000000000&&
            typeof item.emoji?.name==='string').map(item=>({name:text(item.emoji.name,64)||'Emoji',emojiId:id(item.emoji.id)?item.emoji.id:null,
                count:item.count,isOwn:item.me===true||item.me_burst===true}));
    }
    function stickers(value){
        if(!Array.isArray(value))return [];
        return value.slice(0,3).filter(item=>id(item?.id)).map(item=>({name:text(item.name,120)||'Sticker',
            imageUrl:[1,2].includes(item.format_type)?'https://cdn.discordapp.com/stickers/'+item.id+'.png':null}));
    }
    function poll(value){
        if(!value||typeof value.question?.text!=='string'||!Array.isArray(value.answers))return null;
        const counts=new Map((Array.isArray(value.results?.answer_counts)?value.results.answer_counts:[]).slice(0,10).filter(item=>item&&typeof item==='object').map(item=>[item.id,item.count]));
        return {question:text(value.question.text,300)||'Poll',closed:value.results?.is_finalized===true,
            answers:value.answers.slice(0,10).map(item=>{
                const label=text(item?.poll_media?.text||item?.poll_media?.emoji?.name,95)||'Answer',count=counts.get(item?.answer_id);
                return label+(Number.isSafeInteger(count)&&count>=0&&count<=1000000000?' · '+count:'');
            })};
    }
    function systemText(value){
        switch(value.type){
            case 1:return 'Added a recipient to the group';
            case 2:return 'Removed a recipient from the group';
            case 3:return 'Voice call';
            case 4:return 'Changed the group name';
            case 5:return 'Changed the group icon';
            case 6:return 'Pinned a message';
            case 46:return 'Poll results';
            default:return null;
        }
    }
    function refreshMessageExtras(event,name){
        const key=event?.messageId??event?.message_id,owner=observed,epoch=watchEpoch;
        if(!id(key))return;
        // Dispatcher listeners may precede Discord's stores. Read once after
        // dispatch, never increment counts blindly or issue a per-event GET.
        Promise.resolve().then(()=>{
            if(disposed||observed!==owner||watchEpoch!==epoch||me()?.id!==owner.account)return;
            const base=messageCache.get(key);if(!base)return;
            let stored;try{stored=store('MessageStore')?.getMessage?.(owner.channelId,key);}catch{return;}
            if(!stored)return;
            const field=name.startsWith('MESSAGE_REACTION_')?'reactions':'poll';
            if(stored[field]===undefined)return;
            if(field==='reactions'&&!Array.isArray(stored.reactions))return;
            if(field==='poll'&&stored.poll!==null&&(!Array.isArray(stored.poll?.answers)||typeof stored.poll?.question?.text!=='string'))return;
            const projected=field==='reactions'?reactions(stored.reactions):poll(stored.poll);
            if(JSON.stringify(base[field])===JSON.stringify(projected))return;
            appendChange('upsert',key,cacheMessage({...base,[field]:projected}));invalidate();
        });
    }
    function message(value,expected,channelId){
        if(!id(value?.id)||value.channel_id!==channelId||!id(value.author?.id)||typeof value.content!=='string'||
            !Array.isArray(value.attachments)||value.attachments.length>10||new Set(value.attachments.map(item=>item?.id)).size!==value.attachments.length||
            typeof value.timestamp!=='string'||!Number.isFinite(Date.parse(value.timestamp))||Date.parse(value.timestamp)<0||Date.parse(value.timestamp)>253402300799999)fail('invalid_message');
        const edited=value.edited_timestamp;
        if(edited!=null&&(typeof edited!=='string'||!Number.isFinite(Date.parse(edited))||Date.parse(edited)<0||Date.parse(edited)>253402300799999))fail('invalid_message');
        const messageNonce=typeof value.nonce==='string'?value.nonce: Number.isSafeInteger(value.nonce)&&value.nonce>=0?String(value.nonce):null;
        return {id:value.id,channelId,authorId:value.author.id,authorName:text(value.author.global_name||value.author.globalName||value.author.username,120)||'User',
            avatarUrl:avatar(value.author),content:text(value.content,READ_TEXT_LIMIT,true),contentTruncated:value.content.length>READ_TEXT_LIMIT,
            timestamp:Date.parse(value.timestamp),editedTimestamp:edited==null?null:Date.parse(edited),nonce:nonce(messageNonce)?messageNonce:null,
            attachments:value.attachments.map(attachment),isOwn:value.author.id===expected,mentions:mentions(value.mentions),
            reply:reply(value),embeds:embeds(value.embeds),reactions:reactions(value.reactions),stickers:stickers(value.sticker_items),
            poll:poll(value.poll),systemText:systemText(value)};
    }
    function cacheMessage(value){
        messageCache.delete(value.id);messageCache.set(value.id,value);
        if(messageCache.size>CACHE_LIMIT)messageCache.delete(messageCache.keys().next().value);
        return value;
    }
    function appendChange(kind,key=null,value=null,patch=null){
        changes.push({revision:++messageRevision,kind,id:key,message:value,patch});
        if(changes.length>CHANGE_LIMIT)changeFloor=changes.shift().revision;
    }
    function projectEdit(raw,base){
        // Gateway updates are patches, not complete REST messages. Missing is
        // distinct from an explicit empty string/null/empty attachment array.
        const next={...base};
        if(Object.hasOwn(raw,'content')){
            if(typeof raw.content!=='string')fail('invalid_message');
            next.content=text(raw.content,READ_TEXT_LIMIT,true);next.contentTruncated=raw.content.length>READ_TEXT_LIMIT;
        }
        if(Object.hasOwn(raw,'edited_timestamp')){
            const value=raw.edited_timestamp;
            if(value!=null&&(typeof value!=='string'||!Number.isFinite(Date.parse(value))||Date.parse(value)<0||Date.parse(value)>253402300799999))fail('invalid_message');
            next.editedTimestamp=value==null?null:Date.parse(value);
        }
        if(Object.hasOwn(raw,'attachments')){
            if(!Array.isArray(raw.attachments)||raw.attachments.length>10||new Set(raw.attachments.map(item=>item?.id)).size!==raw.attachments.length)fail('invalid_message');
            next.attachments=raw.attachments.map(attachment);
        }
        if(Object.hasOwn(raw,'mentions'))next.mentions=mentions(raw.mentions);
        if(Object.hasOwn(raw,'referenced_message')||Object.hasOwn(raw,'message_reference')){
            const reference=raw.message_reference??(base.reply?{message_id:base.reply.id}:null);
            next.reply=reply({...raw,message_reference:reference});
        }
        if(Object.hasOwn(raw,'embeds'))next.embeds=embeds(raw.embeds);
        if(Object.hasOwn(raw,'reactions'))next.reactions=reactions(raw.reactions);
        if(Object.hasOwn(raw,'sticker_items'))next.stickers=stickers(raw.sticker_items);
        if(Object.hasOwn(raw,'poll'))next.poll=poll(raw.poll);
        if(Object.hasOwn(raw,'type'))next.systemText=systemText(raw);
        return next;
    }
    function recordChanges(event,name){
        if(name==='MESSAGE_DELETE'||name==='MESSAGE_DELETE_BULK'){
            const keys=name==='MESSAGE_DELETE_BULK'?event?.ids:[event?.id??event?.message?.id];
            if(!Array.isArray(keys)||keys.length>CHANGE_LIMIT||keys.some(key=>!id(key))){appendChange('gap');return;}
            for(const key of new Set(keys)){messageCache.delete(key);appendChange('remove',key);}
            return;
        }
        const raw=event?.message,key=raw?.id;
        if(!id(key)){appendChange('gap');return;}
        try{
            let value;
            if(name==='MESSAGE_UPDATE'){
                let base=messageCache.get(key);
                if(!base){
                    // Optional local store: never fetch HTTP for a partial event.
                    try{const stored=store('MessageStore')?.getMessage?.(observed.channelId,key);if(stored)base=message(stored,observed.account,observed.channelId);}catch{}
                }
                if(!base){
                    const prior=changes.findLast(value=>value.id===key);
                    if(prior?.kind==='remove'){appendChange('remove',key);return;}
                    // A first history response may still be in flight. Retain
                    // just the bounded patch so that response supplies its base.
                    const patch=projectEdit(raw,prior?.kind==='patch'?prior.patch:{});
                    appendChange('patch',key,null,patch);return;
                }
                value=projectEdit(raw,base);
            }else value=message(raw,observed.account,observed.channelId);
            appendChange('upsert',key,cacheMessage(value));
        }catch{appendChange('gap',key);}
    }
    function readChanges(args){
        privateChannel(args?.account,args?.channelId);
        if(!observed||observed.account!==args.account||observed.channelId!==args.channelId||args.watchEpoch!==watchEpoch)fail('message_watch_changed');
        const after=args.afterRevision,limit=pageLimit(args.limit);
        if(!Number.isSafeInteger(after)||after<0||after>messageRevision)fail('invalid_message_revision');
        const values=changes.filter(value=>value.revision>after).slice(0,limit);
        const revision=values.at(-1)?.revision??messageRevision;
        return {account:args.account,channelId:args.channelId,watchEpoch,fromRevision:after,revision,
            resetRequired:after<changeFloor||values.some(value=>value.kind==='gap'),hasMore:revision<messageRevision,
            items:values.filter(value=>value.kind==='upsert'||value.kind==='remove')};
    }
    async function recoverMessages(args){
        args={account:args?.account,channelId:args?.channelId,watchEpoch:args?.watchEpoch,ids:Array.isArray(args?.ids)?[...args.ids]:null};
        privateChannel(args?.account,args?.channelId);
        if(!observed||observed.account!==args.account||observed.channelId!==args.channelId||args.watchEpoch!==watchEpoch)fail('message_watch_changed');
        if(!Array.isArray(args.ids)||args.ids.length<1||args.ids.length>40||args.ids.some(key=>!id(key))||new Set(args.ids).size!==args.ids.length)fail('invalid_message_window');
        const keys=[...args.ids].sort((a,b)=>BigInt(a)<BigInt(b)?-1:1),epoch=watchEpoch,revision=messageRevision;
        const upper=keys.at(-1),before=(BigInt(upper)+1n).toString();
        if(!id(before))fail('invalid_message_window');
        const page=await history({account:args.account,channelId:args.channelId,before,limit:PAGE_SIZE});
        account(args.account);
        if(!observed||observed.channelId!==args.channelId||epoch!==watchEpoch)fail('message_watch_changed');
        const values=new Map(page.items.map(value=>[value.id,value]));
        const covered=key=>!page.hasMore||page.nextBefore!==null&&BigInt(key)>=BigInt(page.nextBefore);
        const items=keys.flatMap(key=>values.has(key)?[{revision,kind:'upsert',id:key,message:values.get(key)}]:
            covered(key)?[{revision,kind:'remove',id:key,message:null}]:[]);
        return {account:args.account,channelId:args.channelId,watchEpoch:epoch,revision,after:upper,
            complete:keys.every(covered),items};
    }
    function errorCode(error){
        const status=error?.status??error?.response?.status;
        if(status===429)return 'rate_limited';
        if(status===403)return 'message_forbidden';
        if(status===404)return 'conversation_unavailable';
        if(status===400||status===413)return 'message_rejected';
        return 'message_unconfirmed';
    }
    function body(response){
        if(!response||response.ok===false||Number.isInteger(response.status)&&(response.status<200||response.status>=300))
            throw Object.assign(new Error('request_failed'),{status:response?.status});
        if(!Object.hasOwn(response,'body'))fail('invalid_message_response');
        return response.body;
    }
    async function history(args){
        args={account:args?.account,channelId:args?.channelId,limit:args?.limit,before:args?.before,after:args?.after,around:args?.around};
        privateChannel(args?.account,args?.channelId);
        const api=rest();if(!api)fail('messaging_unavailable');
        const limit=pageLimit(args.limit), before=args.before??null, after=args.after??null, around=args.around??null;
        if([before,after,around].some(value=>value!==null&&!id(value))||[before,after,around].filter(value=>value!==null).length>1)fail('invalid_page');
        const query={limit};if(before)query.before=before;if(after)query.after=after;if(around)query.around=around;
        const epoch=watchEpoch,startedRevision=messageRevision;
        let values;
        try{values=body(await api.get({url:'/channels/'+args.channelId+'/messages',query,retries:0}));}
        catch(error){account(args.account);fail(error.adapterCode??errorCode(error));}
        privateChannel(args.account,args.channelId);
        if(!Array.isArray(values)||values.length>limit)fail('invalid_message_response');
        let items=values.map(value=>message(value,args.account,args.channelId));
        if(new Set(items.map(item=>item.id)).size!==items.length||before&&items.some(item=>BigInt(item.id)>=BigInt(before))||
            after&&items.some(item=>BigInt(item.id)<=BigInt(after)))fail('invalid_message_response');
        const hasMore=items.length===limit;
        const nextBefore=items.length?items.reduce((a,b)=>BigInt(a.id)<BigInt(b.id)?a:b).id:null;
        const nextAfter=items.length?items.reduce((a,b)=>BigInt(a.id)>BigInt(b.id)?a:b).id:null;
        if(observed?.account===args.account&&observed.channelId===args.channelId&&epoch===watchEpoch){
            if(startedRevision<changeFloor)fail('message_history_changed');
            const newer=new Map(changes.filter(value=>value.revision>startedRevision).map(value=>[value.id,value]));
            items=items.flatMap(value=>{const change=newer.get(value.id);return change?.kind==='remove'?[]:
                [cacheMessage(change?.kind==='upsert'?change.message:change?.kind==='patch'?{...value,...change.patch}:value)];});
        }
        items.sort((a,b)=>BigInt(a.id)<BigInt(b.id)?-1:BigInt(a.id)>BigInt(b.id)?1:0);
        return {account:args.account,channelId:args.channelId,before,after,around,nextBefore,
            nextAfter,hasMore,items};
    }
    async function openDm(args){
        args={account:args?.account,userId:args?.userId};
        account(args?.account);
        const relations=relationships(), api=rest();
        if(!id(args.userId)||args.userId===args.account||!relations||relations.isFriend?.(args.userId)!==true||relations.isBlockedOrIgnored(args.userId)!==false)
            fail('recipient_unavailable');
        if(!api)fail('messaging_unavailable');
        const existing=store('ChannelStore')?.getDMFromUserId?.(args.userId);
        if(id(existing))return {account:args.account,channelId:privateChannel(args.account,existing,true).id,userId:args.userId};
        let result;
        try{result=body(await api.post({url:'/users/@me/channels',body:{recipient_id:args.userId},retries:0}));}
        catch(error){account(args.account);fail(error.adapterCode??errorCode(error));}
        account(args.account);
        if(!id(result?.id)||result.type!==1||!Array.isArray(result.recipients)||result.recipients.length!==1||result.recipients[0]?.id!==args.userId)
            fail('invalid_conversation_response');
        if(relations.isFriend?.(args.userId)!==true||relations.isBlockedOrIgnored(args.userId)!==false)fail('recipient_unavailable');
        // A validated REST receipt permits immediate use while the gateway publishes
        // the new DM. This short-lived authority never writes to Discord's stores.
        for(const[key,value]of openingChannels)if(value.expiresAt<=Date.now()||value.account!==args.account)openingChannels.delete(key);
        if(openingChannels.size>=10)openingChannels.delete(openingChannels.keys().next().value);
        openingChannels.set(result.id,{account:args.account,expiresAt:Date.now()+30000,
            channel:{id:result.id,type:1,recipients:[args.userId]}});
        return {account:args.account,channelId:result.id,userId:args.userId};
    }
    function sendKey(args){return args.account+':'+args.channelId+':'+args.nonce;}
    function result(args,status,value=null,error=null){return {account:args.account,channelId:args.channelId,nonce:args.nonce,status,message:value,errorCode:error};}
    function admitLedger(){
        if(ledger.size<LEDGER_LIMIT)return;
        for(const [key,entry]of ledger){
            if(entry.outcome.status==='sent'||entry.outcome.status==='failed'){ledger.delete(key);return;}
        }
        fail('message_send_capacity'); // Unknown external outcomes are never silently dropped to make room for replay.
    }
    function releaseSettledBatch(entry){
        if(entry?.batchId&&['sent','failed'].includes(entry.outcome.status))prepared.delete(entry.batchId);
    }
    function uploadUrl(value){
        if(typeof value!=='string'||value.length>4096)return null;
        try{
            const parsed=new URL(value);
            return parsed.protocol==='https:'&&parsed.hostname==='discord-attachments-uploads-prd.storage.googleapis.com'&&
                !parsed.username&&!parsed.password&&!parsed.hash&&(!parsed.port||parsed.port==='443')&&parsed.pathname!=='/'?parsed.href:null;
        }catch{return null;}
    }
    function uploadedName(value){return typeof value==='string'&&value.length>0&&value.length<=1024&&
        !/[\u0000-\u001f\u007f-\u009f\\:]/.test(value)&&value.split('/').every(segment=>segment!==''&&segment!=='.'&&segment!=='..');}
    async function prepareAttachments(args){
        // Discord's first-party presigned-upload route is private, not an official
        // developer API contract. Validate every receipt and fail closed on drift.
        args={account:args?.account,channelId:args?.channelId,
            files:Array.isArray(args?.files)?args.files.map(file=>({id:file?.id,fileName:file?.fileName,byteLength:file?.byteLength})):null};
        privateChannel(args?.account,args?.channelId,true);
        const api=rest();if(!api)fail('messaging_unavailable');
        const files=args.files, maximum=uploadLimit();
        if(!Array.isArray(files)||files.length<1||files.length>10||new Set(files.map(file=>file?.id)).size!==files.length||
            files.some(file=>!fileId(file?.id)||!fileName(file.fileName)||!Number.isSafeInteger(file.byteLength)||file.byteLength<0||
                file.byteLength>2147483648||maximum!==null&&file.byteLength>maximum)||
            files.reduce((total,file)=>total+file.byteLength,0)>2147483648)fail('invalid_attachments');
        const now=Date.now();
        for(const[key,batch]of prepared)if(batch.expiresAt<=now)prepared.delete(key);
        if(prepared.size+pendingPreparations>=8)fail('attachment_capacity');
        ++pendingPreparations;
        try{
        let response;
        try{
            response=body(await api.post({url:'/channels/'+args.channelId+'/attachments',
                body:{files:files.map(file=>({id:file.id,filename:file.fileName,file_size:file.byteLength}))},retries:0}));
        }catch(error){account(args.account);fail(error.adapterCode??errorCode(error));}
        privateChannel(args.account,args.channelId,true);
        if(!Array.isArray(response?.attachments)||response.attachments.length!==files.length||
            new Set(response.attachments.map(value=>String(value?.id))).size!==files.length)fail('invalid_upload_response');
        const targets=files.map(file=>{
            const target=response.attachments.find(value=>String(value.id)===file.id);
            const url=uploadUrl(target?.upload_url);
            if(!target||!url||!uploadedName(target.upload_filename))fail('invalid_upload_response');
            return {...file,uploadUrl:url,uploadedFileName:target.upload_filename};
        });
        const batchId=crypto.randomUUID().replaceAll('-','');
        const receipt={account:args.account,channelId:args.channelId,batchId,expiresAt:Date.now()+15*60*1000,files:targets};
        prepared.set(batchId,{...receipt,files:targets.map(value=>({...value})),nonce:null});
        return receipt;
        }finally{--pendingPreparations;}
    }
    function validateAttachments(args){
        const values=args.attachments??[];
        if(!Array.isArray(values)||values.length>10||new Set(values.map(value=>value?.id)).size!==values.length)fail('invalid_attachments');
        if(values.length===0){if(args.attachmentBatchId!=null)fail('invalid_attachments');return {values:[],batch:null};}
        const batch=typeof args.attachmentBatchId==='string'?prepared.get(args.attachmentBatchId):null;
        if(!batch||batch.account!==args.account||batch.channelId!==args.channelId||batch.expiresAt<=Date.now()||
            batch.nonce!==null&&batch.nonce!==args.nonce||values.length!==batch.files.length)fail('attachment_batch_unavailable');
        const admitted=values.map(value=>{
            const target=batch.files.find(file=>file.id===value?.id);
            if(!target||value.fileName!==target.fileName||value.uploadedFileName!==target.uploadedFileName)fail('invalid_attachments');
            return {id:target.id,filename:target.fileName,uploaded_filename:target.uploadedFileName};
        });
        return {values:admitted,batch};
    }
    async function sendMessage(args){
        args={account:args?.account,channelId:args?.channelId,nonce:args?.nonce,content:args?.content,attachmentBatchId:args?.attachmentBatchId,
            attachments:Array.isArray(args?.attachments)?args.attachments.map(value=>({id:value?.id,fileName:value?.fileName,uploadedFileName:value?.uploadedFileName})):args?.attachments};
        privateChannel(args?.account,args?.channelId,true);
        if(!nonce(args.nonce)||typeof args.content!=='string'||args.content.length>SEND_LIMIT||
            !args.content.trim()&&!(Array.isArray(args.attachments)&&args.attachments.length>0))fail('invalid_message');
        if(text(args.content,SEND_LIMIT,true)!==args.content)fail('invalid_message');
        const api=rest();if(!api)fail('messaging_unavailable');
        const key=sendKey(args), signature=JSON.stringify([args.content,args.attachmentBatchId??null,args.attachments??[]]);
        const prior=ledger.get(key);
        if(prior){if(prior.signature!==signature)fail('message_nonce_reused');return prior.outcome;}
        const attachments=validateAttachments(args);
        admitLedger();
        if(attachments.batch)attachments.batch.nonce=args.nonce;
        const entry={signature,batchId:args.attachmentBatchId??null,outcome:result(args,'pending')};ledger.set(key,entry);
        try{
            // Recheck immediately before the only mutating call. Auth stays in the
            // provider; no automatic retries are safe after an ambiguous response.
            privateChannel(args.account,args.channelId,true);
            const response=body(await api.post({url:'/channels/'+args.channelId+'/messages',body:{content:args.content,
                nonce:args.nonce,enforce_nonce:true,tts:false,allowed_mentions:{parse:[]},attachments:attachments.values},retries:0}));
            const sent=message(response,args.account,args.channelId);
            if(!sent.isOwn||sent.nonce!==args.nonce)fail('invalid_message_response');
            entry.outcome=result(args,'sent',sent);
            if(me()?.id===args.account&&observed?.account===args.account&&observed.channelId===args.channelId)cacheMessage(sent);
        }catch(error){
            const code=errorCode(error);
            if(entry.outcome.status!=='sent')
                entry.outcome=result(args,['rate_limited','message_forbidden','conversation_unavailable','message_rejected'].includes(code)?'failed':'unknown',null,code);
        }
        // Record external completion even when ownership changed while awaiting it,
        // but never publish another account's result into the current UI.
        releaseSettledBatch(entry);
        account(args.account);
        return entry.outcome;
    }
    async function reconcileSend(args){
        args={account:args?.account,channelId:args?.channelId,nonce:args?.nonce};
        privateChannel(args?.account,args?.channelId);
        if(!nonce(args.nonce))fail('invalid_message');
        const entry=ledger.get(sendKey(args));
        if(entry&&['sent','failed'].includes(entry.outcome.status))return entry.outcome;
        const page=await history({account:args.account,channelId:args.channelId,limit:PAGE_SIZE});
        if(entry?.outcome.status==='sent')return entry.outcome;
        const sent=page.items.find(item=>item.isOwn&&item.nonce===args.nonce);
        const outcome=sent?result(args,'sent',sent):result(args,'unknown',null,'message_unconfirmed');
        if(entry){entry.outcome=outcome;releaseSettledBatch(entry);}
        return outcome;
    }
    return Object.freeze({state,observe,readChanges,recoverMessages,notificationTarget,conversations,history,openDm,prepareAttachments,sendMessage,reconcileSend,
        dispose(){disposed=true;detachObserver();detachNotificationObserver();observed=null;changes.length=0;messageCache.clear();catalogFingerprint='';ledger.clear();prepared.clear();openingChannels.clear();}});
};
