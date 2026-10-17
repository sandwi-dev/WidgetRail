-- Only generate queries. Authentication, listings and purchases remain on the trade website.
local M={}
local function create(class,...)
  if common.classes.Tooltip.Tooltip then local object=new(class);object[class](object,...);return object end
  return new(class,...)
end
local metrics={balanced={{stat='FullDPS',label='Full DPS',weightMult=1},{stat='TotalEHP',label='Effective Hit Pool',weightMult=.5}},
  damage={{stat='FullDPS',label='Full DPS',weightMult=1}},life={{stat='Life',label='Life',weightMult=1}},
  energyshield={{stat='EnergyShield',label='Energy Shield',weightMult=1}},defence={{stat='TotalEHP',label='Effective Hit Pool',weightMult=1}}}
function M.generate(req)
  local slot=assert(build.itemsTab.slots[req.slot],'Select an equipment slot')
  assert(slot:IsShown()and not slot.inactive,'This slot is not active')
  assert(build.itemsTab.items[slot.selItemId],'Equip an item in this slot first so PoB can search the right item type')
  local weights=assert(metrics[req.priority],'Choose a supported priority')
  assert(type(req.budget)=='number'and req.budget>=0 and req.budget<=1000000000,'Enter a valid budget')
  assert(req.currency=='divine'or req.currency=='chaos'or req.currency=='exalted','Choose a currency')
  local generator=create('TradeQueryGenerator',build.itemsTab.tradeQuery)
  generator.tradeTypeIndex=assert(({securable=1,available=2,onlineleague=3,online=4,any=5})[req.mode or 'securable'],'Choose a supported trade method')
  local query,queryError
  generator.requesterContext={}
  generator.requesterCallback=function(_,result,err)query=result;queryError=err end
  local popup,close=main.OpenPopup,main.ClosePopup
  main.OpenPopup=function(_,width,height,title,controls)return{controls=controls}end
  main.ClosePopup=function()end
  local ok,err=pcall(function()
    generator:StartQuery(slot,{statWeights=copyTable(weights),maxPrice=req.budget>0 and req.budget or nil,maxPriceType=req.currency,
      maxLevel=build.characterLevel,includeMirrored=false,includeCorrupted=false,includeRunes=false,
      includeTalisman=false,includeScourge=false,includeEldritch='None',influence1=1,influence2=1,jewelType='Any'})
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
