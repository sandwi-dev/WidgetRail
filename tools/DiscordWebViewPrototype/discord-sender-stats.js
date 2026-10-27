// Discord-private diagnostics belong to the provider, never the host transport.
(() => {
    if(location.origin!=='https://discord.com'||window!==top)return;
    globalThis.WidgetRailDiscordSenderStats={async read(){
        try{
            const entries=[],seen=new Set();
            const connections=globalThis.WidgetRailWebWebpack?.findStore('MediaEngineStore')?.getMediaEngine?.()?.connections??[];
            for(const connection of connections){
                if(connection.context!=='stream')continue;
                const pc=Object.values(connection).find(v=>v&&typeof v.getSenders==='function'&&typeof v.getStats==='function');
                if(!pc||seen.has(pc))continue;seen.add(pc);
                for(const r of(await pc.getStats()).values())if(r.type==='outbound-rtp'&&r.kind==='video')
                    entries.push({width:r.frameWidth,height:r.frameHeight,fps:r.framesPerSecond,frames:r.framesEncoded,bytes:r.bytesSent,limitation:r.qualityLimitationReason});
            }
            return entries.slice(0,8);
        }catch{return [];}
    }};
})();
