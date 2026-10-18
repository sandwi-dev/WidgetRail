-- Only generate queries. Authentication, listings and purchases remain on the trade website.
local compatibility=dofile((debug.getinfo(1,'S').source:sub(2):match('^(.*[/\\])')or '')..'engine-compatibility.lua')
local M={}
local create=compatibility.create
local metrics={balanced={{stat='FullDPS',label='Full DPS',weightMult=1},{stat='TotalEHP',label='Effective Hit Pool',weightMult=.5}},
  damage={{stat='FullDPS',label='Full DPS',weightMult=1}},life={{stat='Life',label='Life',weightMult=1}},
  energyshield={{stat='EnergyShield',label='Energy Shield',weightMult=1}},defence={{stat='TotalEHP',label='Effective Hit Pool',weightMult=1}}}
M.create=create
function M.weights(req)
  if req.priority~='custom'then return copyTable(assert(metrics[req.priority],'Choose a supported priority'))end
  assert(type(req.weights)=='table'and #req.weights<=20,'Choose up to 20 weighted statistics')
  local weights={};local seen={}
  for _,entry in ipairs(req.weights)do
    local stat=data.powerStatList[entry.id]
    assert(stat and not stat.ignoreForItems and stat.label~='Name'and not seen[entry.id],'Choose an available statistic')
    assert(type(entry.weight)=='number'and entry.weight>=0 and entry.weight<=100,'Weights must be between 0 and 100')
    seen[entry.id]=true
    if entry.weight>0 then local w=copyTable(stat);w.weightMult=entry.weight;weights[#weights+1]=w end
  end
  assert(#weights>0,'Set at least one weight greater than zero');return weights
end
function M.catalog(req,clean)
  local stats,slots,bases={},{},{}
  for id,stat in ipairs(data.powerStatList)do if not stat.ignoreForItems and stat.label~='Name'then stats[#stats+1]={id=id,title=clean(stat.label)}end end
  for _,slot in ipairs(build.itemsTab.orderedSlots)do if slot:IsShown()and not slot.inactive then
    local item=build.itemsTab.items[slot.selItemId]
    slots[#slots+1]={name=slot.slotName,title=slot.slotName..' — '..(item and clean(item.name or item.baseName)or'Empty'),empty=not item}
  end end
  local slot=build.itemsTab.slots[req.slot or'']
  if slot and not build.itemsTab.items[slot.selItemId]then
    for _,kind in ipairs(build.data.itemBaseTypeList)do
      for _,base in ipairs(build.data.itemBaseLists[kind]or{})do
        local item=create('Item','Rarity: NORMAL\n'..base.name)
        if item.base and build.itemsTab:IsItemValidForSlot(item,slot.slotName)then
          bases[#bases+1]={id=#bases+1,title=clean(kind),base=base.name};break
        end
      end
    end
  end
  return {stats=stats,slots=slots,bases=bases}
end
function M.generate(req)
  local slot=assert(build.itemsTab.slots[req.slot],'Select an equipment slot')
  assert(slot:IsShown()and not slot.inactive,'This slot is not active')
  local weights=M.weights(req)
  assert(type(req.budget)=='number'and req.budget>=0 and req.budget<=1000000000,'Enter a valid budget')
  assert(req.currency=='divine'or req.currency=='chaos'or req.currency=='exalted','Choose a currency')
  local queryOwner=build.itemsTab.tradeQuery
  if not build.itemsTab.items[slot.selItemId]then
    local catalog=M.catalog(req,function(s)return s end)
    local choice
    for _,base in ipairs(catalog.bases)do if base.base==req.base then choice=base end end
    if not choice and #catalog.bases==1 then choice=catalog.bases[1]end
    assert(choice,'Choose an item category for this empty slot')
    local basis=create('Item','Rarity: NORMAL\n'..choice.base)
    -- A detached category reference for upstream query generation, never equipped.
    local items=setmetatable({[-1]=basis},{__index=build.itemsTab.items})
    local tab=setmetatable({items=items},{__index=build.itemsTab})
    queryOwner=setmetatable({itemsTab=tab},{__index=queryOwner})
    slot=setmetatable({selItemId=-1},{__index=slot})
  end
  local generator=create('TradeQueryGenerator',queryOwner)
  generator.tradeTypeIndex=assert(({securable=1,available=2,onlineleague=3,online=4,any=5})[req.mode or 'securable'],'Choose a supported trade method')
  local query,queryError
  generator.requesterContext={}
  generator.requesterCallback=function(_,result,err)query=result;queryError=err end
  local popup,close=main.OpenPopup,main.ClosePopup
  main.OpenPopup=function(_,width,height,title,controls)return{controls=controls}end
  main.ClosePopup=function()end
  local ok,err=pcall(function()
    local advanced=req.advanced or{}
    for _,key in ipairs({'maxLevel','sockets','links'})do assert(not advanced[key]or(type(advanced[key])=='number'and advanced[key]>=0 and advanced[key]<=100),'Invalid '..key)end
    generator:StartQuery(slot,{statWeights=weights,maxPrice=req.budget>0 and req.budget or nil,maxPriceType=req.currency,
      maxLevel=advanced.maxLevel or build.characterLevel,includeMirrored=advanced.includeMirrored==true,includeCorrupted=advanced.includeCorrupted==true,includeRunes=advanced.includeRunes==true,
      includeTalisman=advanced.includeTalisman==true,includeScourge=advanced.includeScourge==true,includeEldritch=advanced.includeEldritch and'Keep regular'or'None',
      sockets=advanced.sockets,links=advanced.links,influence1=1,influence2=1,jewelType='Any'})
    assert(generator.calcContext.co,'Weighted search is not supported for this item type')
    local co=generator.calcContext.co
    while coroutine.status(co)~='dead'do local resumed,reason=coroutine.resume(co,generator);assert(resumed,reason)end
    generator.calcContext.co=nil;generator:FinishQuery()
    assert(not queryError,queryError);assert(query,'PoB did not produce a trade search')
  end)
  main.OpenPopup,main.ClosePopup=popup,close
  assert(ok,err)
  if type(query)=='string'then query=assert(require('dkjson').decode(query))end
  return query
end
return M
