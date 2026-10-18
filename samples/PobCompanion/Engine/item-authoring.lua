-- Item identity, affix eligibility, ranges and calculations remain upstream.
local compatibility=dofile((debug.getinfo(1,'S').source:sub(2):match('^(.*[/\\])')or '')..'engine-compatibility.lua')
local M={}
local itemSettings
function M.configure(settings)itemSettings=settings end
local function index(id,list)assert(type(id)=='number'and id%1==0 and list[id],'This item option no longer exists');return list[id]end
local function withDialog(action,accept)
  local popup,close=main.OpenPopup,main.ClosePopup;local visited=false;local accepted
  main.OpenPopup=function(_,width,height,title,controls)visited=true;accepted=accept(controls);return accepted end
  main.ClosePopup=function()end
  local ok,result=pcall(action);main.OpenPopup,main.ClosePopup=popup,close
  assert(ok,result);assert(visited,'The upstream editor did not open');return accepted
end
local function display(req)
  local tab=build.itemsTab
  if req.itemId then tab:SetDisplayItem(assert(tab.items[req.itemId],'This item no longer exists'))
  else assert(type(req.raw)=='string'and #req.raw<=32768,'Invalid item draft');tab:CreateDisplayItemFromRaw(req.raw)end
  return assert(tab.displayItem and tab.displayItem.base and tab.displayItem,'PoB did not recognize the item')
end
function M.bases(clean)
  local rows={}
  for _,kind in ipairs(build.data.itemBaseTypeList)do for _,base in ipairs(build.data.itemBaseLists[kind]or{})do
    rows[#rows+1]={id=#rows+1,title=clean(base.name),description=clean(kind),kind=kind,base=base.name}
  end end
  return rows
end
local function uniqueItems()
  local frames=0
  while main.uniqueDB.loading do
    local load=assert(main.onFrameFuncs.LoadItems,'PoB has not initialized its unique library')
    load();frames=frames+1;assert(frames<10000,'Unique library did not finish loading')
  end
  return main.uniqueDB.list
end
function M.uniques(clean)
  local rows={}
  for name,item in pairs(uniqueItems())do
    rows[#rows+1]={key=name,title=clean(name),hasVariants=(item.variantList and #item.variantList>1)or false,description=clean(item.baseName)..' · '..clean(item.type)..
      (item.source and '\n'..clean(build.itemsTab:FormatItemSource(item.source))or'')}
  end
  table.sort(rows,function(a,b)return a.title<b.title end)
  for i,row in ipairs(rows)do row.id=i end
  return rows
end
function M.uniqueDraft(req,clean,textRuns)
  assert(type(req.key)=='string'and #req.key<=512,'Choose a unique item')
  local source=assert(uniqueItems()[req.key],'This unique is not in the current engine library')
  -- Reparse a copy: draft edits must never mutate PoB's shared database item.
  return M.inspect({raw=source:BuildRaw()},clean,textRuns)
end
local function create(req)
  local tab=build.itemsTab
  withDialog(function()tab:CraftItem()end,function(controls)
    local typeIndex;for i,name in ipairs(controls.type.list)do if name==req.kind then typeIndex=i end end
    assert(typeIndex,'Choose an item type');controls.type:SetSel(typeIndex)
    local baseIndex;for i,base in ipairs(controls.base.list)do if base.name==req.base then baseIndex=i end end
    assert(baseIndex,'Choose an item base');controls.base:SetSel(baseIndex)
    local rarity;for i,entry in ipairs(controls.rarity.list)do if entry.rarity==(req.rarity or'RARE')then rarity=i end end
    assert(rarity,'Choose a supported rarity');controls.rarity:SetSel(rarity)
    controls.save.onClick()
  end)
  return assert(tab.displayItem,'PoB could not create this item')
end
local function affixChoices(item,slot,clean)
  local tab=build.itemsTab;assert(item.crafted and slot>=1 and slot<=item.affixLimit,'This item has no editable affix in that position')
  tab:UpdateAffixControls();local drop=tab.controls['displayItemAffix'..slot]
  local result={{id=1,title='None',description='Remove this affix',modId='None'}}
  for _,group in ipairs(drop.list)do if type(group)=='table'then for _,modId in ipairs(group.modList or{group.modId})do
    local mod=assert(item.affixes[modId]);result[#result+1]={id=#result+1,title=clean(table.concat(mod,' / ')),
      description=clean((mod.affix or modId)..' · Required item level '..(mod.level or 1)),modId=modId}
  end end end
  return result,drop
end
local function modifierDialog(req,accept)
  local tab=build.itemsTab
  assert(tab.controls.displayItemAddCustom:IsShown(),'This item does not support added modifiers')
  return withDialog(function()return tab:AddCustomModifierToDisplayItem()end,function(controls)
    if req.source then
      local selected;for i,entry in ipairs(controls.source.list)do if entry.sourceId==req.source then selected=i end end
      assert(selected and req.source~='CUSTOM','This modifier source is not available for this item')
      controls.source:SetSel(selected)
    end
    return accept(controls)
  end)
end
function M.modifiers(req,clean)
  display(req)
  return modifierDialog(req,function(controls)
    local sources={}
    for _,source in ipairs(controls.source.list)do if source.sourceId~='CUSTOM'then
      sources[#sources+1]={key=source.sourceId,title=clean(source.label)}
    end end
    local selected=controls.source.list[controls.source.selIndex].sourceId
    local choices={}
    if selected~='CUSTOM'then for i,entry in ipairs(controls.modSelect.list)do
      local label=clean(entry.label)
      choices[#choices+1]={id=i,title=label:match('^(.-)%s+%[')or label,
        description=clean(table.concat(entry.mod,'\n'))..(entry.mod.level and'\nRequired item level '..entry.mod.level or''),key=entry.label}
    end end
    return {sources=sources,source=selected,choices=choices}
  end)
end
function M.inspect(req,clean,textRuns)
  local item=display(req);local tab=build.itemsTab
  local result={raw=item:BuildRaw(),name=clean(item.name or item.baseName),rarity=item.rarity,crafted=item.crafted==true,
    canAddCustom=tab.controls.displayItemAddCustom:IsShown(),affixes={},custom={},ranges={},details={},controls=itemSettings.inspectDisplayItem(textRuns,clean).controls}
  if item.crafted then
    tab:UpdateAffixControls()
    for i=1,item.affixLimit do
      local drop=tab.controls['displayItemAffix'..i];local affix=item[drop.outputTable][drop.outputIndex];local selected=drop.list[drop.selIndex]
      result.affixes[#result.affixes+1]={id=i,title=(drop.outputTable=='prefixes'and'Prefix 'or'Suffix ')..drop.outputIndex,
        selected=clean(type(selected)=='table'and selected.label or selected),roll=math.floor((affix.range or .5)*100+.5),modId=affix.modId}
    end
  end
  for i=1,item.customCount or 0 do
    local c=tab.controls['displayItemCustomModifier'..i]
    result.custom[#result.custom+1]={id=i,text=clean(c.label),runs=textRuns(c.label)}
  end
  for i,line in ipairs(item.rangeLineList or{})do result.ranges[#result.ranges+1]={id=i,title=clean(line.line),roll=math.floor((line.range or .5)*100+.5)}end
  -- Item information is separate from our explicit Compare action. Upstream's
  -- default hover also includes desktop hotkeys and comparisons to other slots.
  local tooltip=compatibility.create('Tooltip')
  local previous=tab.showStatDifferences;tab.showStatDifferences=false
  local ok,err=pcall(tab.AddItemTooltip,tab,tooltip,item,nil,false,600)
  tab.showStatDifferences=previous;assert(ok,err)
  for _,line in ipairs(tooltip.lines)do if line.text and not clean(line.text):match('^Tip:')then
    result.details[#result.details+1]={text=clean(line.text),runs=textRuns(line.text)}
  end end
  return result
end
function M.draft(req,clean,textRuns)
  local item=req.mode=='create'and create(req)or display(req);local tab=build.itemsTab
  if req.mode=='choices'then return{choices=affixChoices(item,req.slot,clean)}end
  if req.mode=='affix'then
    local choices,drop=affixChoices(item,req.slot,clean);local allowed=false
    for _,choice in ipairs(choices)do if choice.modId==req.modId then allowed=true end end
    assert(allowed,'This affix is not available for this item or conflicts with another affix')
    local affix=item[drop.outputTable][drop.outputIndex];affix.modId=req.modId;affix.range=.5;item:Craft()
  elseif req.mode=='roll'then
    local _,drop=affixChoices(item,req.slot,clean)
    assert(type(req.value)=='number'and req.value>=0 and req.value<=100,'Roll must be between 0 and 100 percent')
    item[drop.outputTable][drop.outputIndex].range=req.value/100;item:Craft()
  elseif req.mode=='modifier-add'then
    modifierDialog(req,function(controls)
      local choice=index(req.id,controls.modSelect.list)
      assert(choice.label==req.modId,'The modifier list changed. Choose the modifier again')
      controls.modSelect:SetSel(req.id);controls.save.onClick()
    end)
    item=tab.displayItem
  elseif req.mode=='custom-add'then
    assert(tab.controls.displayItemAddCustom:IsShown(),'This item does not support custom modifiers')
    assert(type(req.text)=='string'and #req.text<=4096 and not req.text:find('[\r\n]'),'Enter one modifier at a time')
    assert(req.text:match('%S'),'Enter a modifier')
    withDialog(function()tab:AddCustomModifierToDisplayItem()end,function(controls)
      local custom;for i,entry in ipairs(controls.source.list)do if entry.sourceId=='CUSTOM'then custom=i end end
      assert(custom,'Custom modifiers are unavailable');controls.source:SetSel(custom);controls.custom:SetText(req.text);controls.save.onClick()
    end)
    item=tab.displayItem
  elseif req.mode=='custom-delete'then
    local c=assert(tab.controls['displayItemCustomModifierRemove'..req.id],'This modifier no longer exists')
    assert(c:IsShown(),'This modifier is no longer visible');c.onClick();item=tab.displayItem
  elseif req.mode=='range'then
    index(req.id,item.rangeLineList);assert(type(req.value)=='number'and req.value>=0 and req.value<=100,'Roll must be between 0 and 100 percent')
    tab.controls.displayItemRangeLine:SetSel(req.id)
    tab.controls.displayItemRangeSlider.changeFunc(req.value/100);item=tab.displayItem
  elseif req.mode=='control'then
    itemSettings.applyDisplayControl(req);item=tab.displayItem
  elseif req.mode~='create'and req.mode~='inspect'then error('Unknown item edit')end
  tab:SetDisplayItem(item)
  local raw=item:BuildRaw();assert(#raw<=32768,'Item draft is too large')
  if req.itemId then item.id=req.itemId;tab:AddItem(item,true);tab:PopulateSlots();tab:AddUndoState();build.buildFlag=true end
  return M.inspect({raw=raw},clean,textRuns)
end
function M.inventory(clean)
  local rows={};local tab=build.itemsTab
  for _,id in ipairs(tab.itemOrderList)do
    local item=tab.items[id];local uses={}
    if item then
      for _,setId in ipairs(tab.itemSetOrderList)do local set=tab.itemSets[setId]
        for _,slot in ipairs(tab.orderedSlots)do if not slot.nodeId and set[slot.slotName]and set[slot.slotName].selItemId==id then uses[#uses+1]=(set.title or'Default')..' · '..slot.slotName end end
      end
      for _,spec in ipairs(build.treeTab.specList)do for _,itemId in pairs(spec.jewels)do if itemId==id then uses[#uses+1]=(spec.title or'Default')..' · Jewel'end end end
      rows[#rows+1]={id=id,title=clean(item.name or item.baseName),base=clean(item.baseName),rarity=item.rarity,uses=uses}
    end
  end
  return rows
end
function M.edit(req)
  local tab=build.itemsTab
  local function add(item)
    item.id=nil;tab:AddItem(item,true);tab:PopulateSlots();tab:AddUndoState();build.buildFlag=true;return item.id
  end
  if req.mode=='add'then return add(display(req))end
  local item=assert(tab.items[req.id],'This item no longer exists')
  if req.mode=='delete'then tab:DeleteItem(item);return 0
  elseif req.mode=='copy'then tab:CreateDisplayItemFromRaw(item:BuildRaw());return add(tab.displayItem)
  elseif req.mode=='equip'then
    local slot=assert(tab.slots[req.slot],'Choose an equipment slot')
    assert(slot:IsShown()and not slot.inactive and tab:IsItemValidForSlot(item,req.slot),'This item does not fit that slot')
    slot:SetSelItemId(item.id);tab:PopulateSlots();tab:AddUndoState();build.buildFlag=true;return item.id
  else error('Unknown inventory action')end
end
return M
