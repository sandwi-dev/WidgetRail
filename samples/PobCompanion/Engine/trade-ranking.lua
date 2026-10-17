-- Desktop PoB owns item-replacement evaluation, weighting and ordering.
local M={};local active
local function finite(value)return type(value)=='number'and value==value and value~=math.huge and value~=-math.huge end
function M.begin(req,search)
  local slot=assert(build.itemsTab.slots[req.slot],'Choose a comparison slot')
  assert(slot:IsShown()and not slot.inactive,'This comparison slot is inactive')
  local q=setmetatable({itemsTab=build.itemsTab,resultTbl={[1]={}},slotTables={[1]=slot},
    statSortSelectionList=search.weights(req),onlyWeightedBaseOutput={[1]={}},lastComparedWeightList={[1]={}},
    sortModes={StatValue='Stat Value'}},{__index=build.itemsTab.tradeQuery})
  q.tradeQueryGenerator=search.create('TradeQueryGenerator',q)
  local calc,base=build.calcsTab:GetMiscCalculator()
  active={query=q,calc=calc,base=base,slot=req.slot,xml=build:SaveDB('code')}
  return {ready=true}
end
function M.item(req,convert,search,clean)
  local a=assert(active,'Start evaluation again');assert(a.xml==build:SaveDB('code'),'The build changed; evaluate again')
  assert(#a.query.resultTbl[1]<100,'Evaluation is limited to 100 items')
  local previousCount=#a.query.resultTbl[1]
  local ok,result=pcall(function()
    local raw=req.raw or convert(req.entry)
    assert(type(raw)=='string'and #raw<=32768,'Invalid item text')
    local item=search.create('Item',raw)
    assert(item.base and build.itemsTab:IsItemValidForSlot(item,a.slot),'This item does not fit the comparison slot')
    local rows=a.query.resultTbl[1];local index=#rows+1;rows[index]={item_string=raw}
    local output
    local success,evaluation=pcall(a.query.GetResultEvaluation,a.query,1,index,function(args)output=a.calc(args);return output end,a.base)
    if not success then rows[index]=nil;error(evaluation)end
    local ev=evaluation[1];assert(ev and finite(ev.weight),'PoB could not calculate a finite score')
    a.query.onlyWeightedBaseOutput[1][index]=a.query:ReduceOutput(a.base)
    a.query.lastComparedWeightList[1][index]=copyTable(a.query.statSortSelectionList)
    local changes={}
    for _,key in ipairs({'CombinedDPS','FullDPS','Life','EnergyShield','Mana','TotalEHP','FireResist','ColdResist','LightningResist','ChaosResist'})do
      local before,after=a.base[key],output[key]
      if finite(before)and finite(after)then changes[#changes+1]={stat=key,before=before,after=after,delta=after-before}end
    end
    local percent=a.query.GetResultScorePercent and a.query:GetResultScorePercent(ev)
    local baseline=a.query.tradeQueryGenerator.WeightedRatioOutputs(a.base,a.base,a.query.statSortSelectionList)
    return {index=index,raw=raw,score=ev.weight,baselineScore=finite(baseline)and baseline or nil,scorePercent=finite(percent)and percent or nil,changes=changes}
  end)
  if not ok then a.query.resultTbl[1][previousCount+1]=nil;return {error=clean(result):sub(1,350)}end
  return result
end
function M.finish()
  local a=assert(active,'Start evaluation again');assert(a.xml==build:SaveDB('code'),'The build changed; evaluate again')
  local sorted,err=a.query:SortFetchResults(1,a.query.sortModes.StatValue)
  assert(sorted,err);local order={};for _,row in ipairs(sorted)do order[#order+1]=row.index end
  active=nil
  return {order=order}
end
return M
