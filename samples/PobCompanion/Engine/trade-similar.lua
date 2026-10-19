-- Adapt upstream Buy similar controls and URL generation without opening UI,
-- copying to the clipboard, fetching leagues or changing equipped items.
local compatibility=dofile((debug.getinfo(1,'S').source:sub(2):match('^(.*[/\\])')or '')..'engine-compatibility.lua')
local M={}
function M.read(req,clean,textRuns)
  assert(type(req.raw)=='string'and #req.raw>0 and #req.raw<=65536,'Choose a valid reference item')
  assert(type(req.league)=='string'and #req.league>0 and #req.league<=160 and not req.league:find('%c'),'Choose a league')
  local item=compatibility.create('Item',req.raw)
  assert(item.base,'PoB did not recognize the reference item')
  local slot=build.itemsTab:GetComparisonSlotNameForItem(item)
  local similar=LoadModule('Classes/CompareBuySimilar')
  local requests=assert(build.itemsTab.tradeQuery.tradeQueryRequests,'Buy similar is unavailable in this engine')
  local fetch,popup,copy,open,deflate=requests.FetchLeagues,main.OpenPopup,Copy,OpenURL,Deflate
  local remembered={similar.lastRealmIdx,similar.lastLeagueIdx,similar.lastListedIndex,similar.lastLeagueByRealm}
  local controls,url,queryJson
  requests.FetchLeagues=function(_,realm,callback)callback({req.league})end
  main.OpenPopup=function(_,width,height,title,value)controls=value;return{controls=value}end
  -- Headless PoB has no gzip encoder. Capture its finished JSON here; the
  -- existing managed trade URL encoder handles the browser transport.
  Deflate=function(value)queryJson=value;return 'captured'end
  Copy=function()end
  OpenURL=function(value)url=value end
  similar.lastRealmIdx,similar.lastLeagueIdx,similar.lastListedIndex,similar.lastLeagueByRealm=nil,nil,nil,nil
  local ok,result=pcall(function()
    similar.openPopup(item,slot,build)
    assert(controls and controls.search and controls.listedDrop,'PoB Buy similar controls changed')
    -- Do not use the desktop popup's shortened display labels for modifier text.
    local mods=similar.addModEntries(item,{{list=item.enchantModLines,type='enchant'},{list=item.implicitModLines,type='implicit'},{list=item.explicitModLines,type='explicit'}})
    local fields={}
    local function field(key,prefix,label,entry)
      local check=controls[prefix..'Check']
      local minimum,maximum=controls[prefix..'Min'],controls[prefix..'Max']
      local supported=not check or check:IsEnabled()
      fields[#fields+1]={key=key,title=clean(label),runs=textRuns(label),supported=supported,enabled=check and check.state==true or false,
        toggle=check~=nil,minimum=minimum and minimum.buf or nil,maximum=maximum and maximum.buf or nil,
        kind=entry and entry.type or 'property'}
      return fields[#fields],check,minimum,maximum
    end
    if controls.baseTypeCheck then
      fields[#fields+1]={key='base',title=clean(controls.baseTypeLabel.label),supported=true,toggle=true,enabled=controls.baseTypeCheck.state==true,kind='property'}
    end
    if controls.ilvlMin then field('ilvl','ilvl','Item level')end
    for i=1,8 do if controls['def'..i..'Check']then field('def'..i,'def'..i,controls['def'..i..'Label'].label)end end
    assert(#mods<=96,'This item has too many searchable modifiers')
    for i,entry in ipairs(mods)do field('mod'..i,'mod'..i,table.concat(entry.formattedLines,'\n'),entry)end
    local known={}
    for _,f in ipairs(fields)do known[f.key]=f end
    for key,value in pairs(req.options or{})do
      local f=assert(known[key],'This search option no longer exists')
      assert(type(value)=='table'and f.supported,'This modifier cannot be searched on trade')
      local prefix=key=='base'and'baseType'or key
      if value.enabled~=nil then assert(f.toggle and type(value.enabled)=='boolean','Invalid filter selection');controls[prefix..'Check'].state=value.enabled end
      for _,bound in ipairs({'minimum','maximum'})do if value[bound]~=nil then
        local control=assert(controls[prefix..(bound=='minimum'and'Min'or'Max')],'This filter has no editable range')
        local input=value[bound]
        assert(type(input)=='string'and #input<=16 and (input==''or input:match('^%-?%d+%.?%d*$')),'Enter a valid numeric bound')
        local number=tonumber(input);assert(input==''or(number and number==number and math.abs(number)<=1000000000),'Invalid numeric bound')
        control.buf=input
      end end
      local lo,hi=controls[prefix..'Min'],controls[prefix..'Max']
      local min,max=lo and tonumber(lo.buf),hi and tonumber(hi.buf)
      assert(not min or not max or min<=max,'Minimum cannot exceed maximum')
    end
    controls.leagueDrop:SetList({req.league});controls.leagueDrop.selIndex=1
    local mode=req.mode or 1
    assert(type(mode)=='number'and mode%1==0 and controls.listedDrop.list[mode],'Choose a listing status')
    controls.listedDrop.selIndex=mode
    controls.listedDrop.selFunc(mode,controls.listedDrop.list[mode]) -- rebuild using upstream logic
    if req.op=='similar-search'then controls.search.onClick();assert(type(url)=='string'and #url<=8192,'The search is too large; select fewer modifiers')end
    if url and not queryJson then queryJson=assert(url:match('[?]q=(.*)$'),'PoB returned an unsupported search URL'):gsub('%%(%x%x)',function(hex)return string.char(tonumber(hex,16))end)end
    local query=queryJson and assert(require('dkjson').decode(queryJson))or nil
    return{query=query,fields=fields,modes=controls.listedDrop.list,name=clean(item.name or item.baseName),base=clean(item.baseName),unique=item.rarity=='UNIQUE'or item.rarity=='RELIC',url=url}
  end)
  requests.FetchLeagues,main.OpenPopup,Copy,OpenURL,Deflate=fetch,popup,copy,open,deflate
  similar.lastRealmIdx,similar.lastLeagueIdx,similar.lastListedIndex,similar.lastLeagueByRealm=unpack(remembered,1,4)
  assert(ok,result)
  return result
end
return M
