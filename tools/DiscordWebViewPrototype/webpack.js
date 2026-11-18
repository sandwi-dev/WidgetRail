// Read loaded modules only. Do not force-execute unknown module factories.
// Discord's private interface can change; missing capabilities fail closed.
(() => {
    if(location.origin!=='https://discord.com'||window!==top)return;
    const runtimes=new Set();
    const cache=new Map();
    const moduleProbe=Symbol('WidgetRail.moduleProbe');
    function values(){
        if(!runtimes.size){const chunks=globalThis.webpackChunkdiscord_app;if(!Array.isArray(chunks))return [];
            const key='widgetrail-probe-'+crypto.randomUUID();chunks.push([[key],{},r=>{runtimes.add(r);}]);chunks.pop();}
        const result=[];
        for(const runtime of runtimes)for(const module of Object.values(runtime.c??{})){
            const e=module.exports;if(!e)continue;result.push(e);
            if(typeof e==='object'||typeof e==='function')for(const key of Object.keys(e)){
                try{if(e[key])result.push(e[key]);}catch{}
            }
        }
        return result;
    }
    function find(key,predicate,validateCached=false){
        if(cache.has(key)){
            const cached=cache.get(key);
            try{if(!validateCached||predicate(cached))return cached;}catch{}
            cache.delete(key);
        }
        for(const v of values()){
            if(!v||!['object','function'].includes(typeof v))continue;
            try{
                // Translation and service proxies manufacture a member for any
                // name. They must never pass a real API capability check.
                if(Reflect.get(v,moduleProbe)!==undefined){Reflect.deleteProperty(v,moduleProbe);continue;}
                if(predicate(v)){cache.set(key,v);return v;}
            }catch{}
        }
        return undefined;
    }
    function member(value,name){
        if(value==null||!['object','function'].includes(typeof value))return false;
        for(let v=value,depth=0;v&&v!==Object.prototype&&depth<6;v=Object.getPrototypeOf(v),depth++)
            if(Object.getOwnPropertyDescriptor(v,name))return value[name]!==undefined;
        return false;
    }
    globalThis.WidgetRailWebWebpack={
        diagnostics:()=>{if(!runtimes.size)values();return {lastCommand:globalThis.WidgetRailLastCommand,runtimes:[...runtimes].map(r=>({keys:Object.keys(r),modules:Object.keys(r.c??{}).length,factories:Object.keys(r.m??{}).length})),chunkCount:globalThis.webpackChunkdiscord_app?.length??0};},
        findStore:name=>find('store:'+name,v=>v.constructor?.displayName===name||(typeof v.getName==='function'&&v.getName()===name)),
        findByProps:(...props)=>{
            // Some unrelated client helpers expose identical method names. A
            // caller may additionally require the actual function contract.
            const predicate=typeof props.at(-1)==='function'?props.pop():null;
            return find('props:'+props.join(',')+(predicate?':where:'+Function.prototype.toString.call(predicate):''),
                v=>props.every(p=>member(v,p))&&(!predicate||predicate(v)),predicate!==null);
        },
        findByCode:pattern=>find('code:'+pattern,v=>typeof v==='function'&&Function.prototype.toString.call(v).includes(pattern)),
        get Common(){return {FluxDispatcher:find('dispatcher',v=>['dispatch','subscribe','wait'].every(p=>member(v,p)&&typeof v[p]==='function'))};}
    };
})();
