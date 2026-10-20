-- Project upstream editor capabilities; callbacks own validation and parsing.
local compatibility=dofile((debug.getinfo(1,'S').source:sub(2):match('^(.*[/\\])')or '')..'engine-compatibility.lua')
local M={}
local function tooltip()return compatibility.create('Tooltip')end
local function index(i,list)assert(type(i)=='number'and i%1==0 and list[i],'Selection no longer exists');return list[i]end
local function controlOptions(control)
  local choices={}
  for i,v in ipairs(control.list or {})do
    choices[#choices+1]={id=i,title=type(v)=='table'and(v.name or v.label or tostring(i))or tostring(v),
      description=type(v)=='table'and type(v.lines)=='table'and table.concat(v.lines,'\n')or '',active=i==control.selIndex}
  end
  return choices
end
local itemControls={
  {'displayItemQualityEdit','Quality'},{'displayItemInfluence','Influence'},{'displayItemInfluence2','Second influence'},
  {'displayItemVersion','Item version'},{'displayItemBaseVariant','Base variant'},
  {'displayItemVariant','Variant'},{'displayItemAltVariant','Variant 2'},{'displayItemAltVariant2','Variant 3'},
  {'displayItemAltVariant3','Variant 4'},{'displayItemAltVariant4','Variant 5'},{'displayItemAltVariant5','Variant 6'},
  {'displayItemCatalyst','Catalyst'},{'displayItemCatalystQualityEdit','Catalyst quality'},
  {'displayItemRune1','Rune 1'},{'displayItemRune2','Rune 2'},{'displayItemRune3','Rune 3'},
  {'displayItemRune4','Rune 4'},{'displayItemRune5','Rune 5'},{'displayItemRune6','Rune 6'},
}
local function item(req)
  local slot=assert(build.itemsTab.slots[req.slot],'Equipment slot no longer exists')
  assert(slot:IsShown()and not slot.inactive,'Equipment slot is not active')
  local value=assert(build.itemsTab.items[slot.selItemId],'This slot is empty')
  build.itemsTab:SetDisplayItem(value)
  return value
end
local function modLines()
  local seen,lines={},{}
  for _,line in ipairs(build.itemsTab.displayItemTooltip.lines)do
    if line.modLine and not seen[line.modLine]then seen[line.modLine]=true;lines[#lines+1]={text=line.text,mod=line.modLine}end
  end
  return lines
end
function M.inspectDisplayItem(textRuns,clean)
  local result={controls={},mods={}}
  for _,entry in ipairs(itemControls)do
    local c=build.itemsTab.controls[entry[1]]
    if c and c:IsShown()then
      local options=controlOptions(c)
      if (entry[1]=='displayItemInfluence'or entry[1]=='displayItemInfluence2')and options[1]then options[1].title='None'end
      result.controls[#result.controls+1]={id=entry[1],title=entry[2],value=c.buf,options=options,enabled=c:IsEnabled()}
    end
  end
  if build.itemsTab.ToggleDisplayItemModLine then
    for i,line in ipairs(modLines())do result.mods[#result.mods+1]={id=i,text=clean(line.text),runs=textRuns(line.text),enabled=not line.mod.disabled}end
  end
  return result
end
function M.inspectItem(req,textRuns,clean)
  item(req);return M.inspectDisplayItem(textRuns,clean)
end
function M.applyDisplayControl(req)
  local allowed=false;for _,entry in ipairs(itemControls)do if entry[1]==req.control then allowed=true end end
  local c=allowed and build.itemsTab.controls[req.control]
  assert(c and c:IsShown()and c:IsEnabled(),'This item setting is not available')
  if req.control=='displayItemCatalystQualityEdit'or req.control=='displayItemQualityEdit'then
    assert(type(req.value)=='number'and req.value%1==0 and req.value>=0 and req.value<=99,'Quality must be between 0 and 99')
    c:SetText(tostring(req.value),true)
  else index(req.value,c.list);c:SetSel(req.value)end
end
function M.snapshot(result,textRuns,clean)
  local tab=build.configTab
  result.customModGroups={}
  for i,block in ipairs(tab.configSets[tab.activeConfigSetId].customModsList or {})do
    local lines={}
    for line in ((block.text or '')..'\n'):gmatch('(.-)\n')do
      if #line>0 then
        local mods,extra=modLib.parseMod(StripEscapes(line):match('^%s*(.-)%s*$'))
        lines[#lines+1]={text=line,supported=mods~=nil and not extra}
      end
    end
    result.customModGroups[#result.customModGroups+1]={id=i,title=clean(block.title),enabled=block.enabled~=false,text=block.text or '',lines=lines}
  end
  result.characterRunes={}
  for _,name in ipairs(build.itemsTab.runeSlotOrder or {})do
    local c=build.itemsTab.runeSlots[name]
    if c:IsShown()then
      result.characterRunes[#result.characterRunes+1]={id=name,title=clean(build.itemsTab.controls[name..' Label']:GetProperty('label')),options=controlOptions(c)}
    end
  end
  result.canEditNodeNotes=result.capabilities.canEditNodeNotes
  result.gemSortMetrics={}
  if build.skillsTab.gemSlots[1].nameSpec.DPSBuilder then
    for _,metric in ipairs(build.skillsTab.controls.sortGemsByDPSFieldControl.list)do result.gemSortMetrics[#result.gemSortMetrics+1]={id=metric.type,title=clean(metric.label)}end
  end
end
function M.gemDetails(req,textRuns,clean)
  local tab=build.skillsTab;local group=index(req.group,tab.socketGroupList);local gem=index(req.gem,group.gemList)
  local old=tab.displayGroup;tab:SetDisplayGroup(group);tab:UpdateGemSlots()
  local picker=tab.gemSlots[req.gem].nameSpec;local previous=picker.tooltip;local t=tooltip()
  t.maxWidth=600;picker.tooltip=t
  local ok,err=pcall(picker.AddGemTooltip,picker,gem)
  picker.tooltip=previous;tab:SetDisplayGroup(old);tab:UpdateGemSlots();assert(ok,err)
  local lines={}
  for _,line in ipairs(t.lines)do
    if line.text then
      local text=clean(line.text)
      if text~='' and not text:match('^Tip:') then
        local kind='metadata'
        for _,entry in ipairs({{'GEMDESCRIPTION','description'},{'MAGIC','effect'},{'UNSUPPORTED','effect'},{'GEM','heading'}})do
          local prefix=colorCodes[entry[1]]
          if prefix and line.text:sub(1,#prefix)==prefix then kind=entry[2];break end
        end
        lines[#lines+1]={text=text,kind=kind,runs=textRuns(line.text)}
      end
    end
  end
  return {name=clean(gem.nameSpec or ''),lines=lines}
end
function M.quality(req,textRuns,clean)
  local tab=build.skillsTab;local g=index(req.group,tab.socketGroupList);index(req.gem,g.gemList)
  local old=tab.displayGroup;tab:SetDisplayGroup(g);tab:UpdateGemSlots()
  local t=tooltip()
  local ok,err=pcall(tab.gemSlots[req.gem].quality.tooltipFunc,t)
  tab:SetDisplayGroup(old);tab:UpdateGemSlots();assert(ok,err)
  local lines={};for _,line in ipairs(t.lines)do if line.text then lines[#lines+1]={text=clean(line.text),runs=textRuns(line.text)}end end
  return lines
end
function M.edit(req)
  if req.op=='custom-mod' then
    local tab=build.configTab;local list=assert(tab.configSets[tab.activeConfigSetId].customModsList,'This engine has no grouped custom modifiers')
    local block=req.id and index(req.id,list)
    if req.mode=='add' then assert(#list<64,'This configuration already has 64 modifier groups');tab.controls.customModsAddBlock.onClick()
    elseif req.mode=='delete' then tab.customModsBlockControls[req.id].controls.deleteBtn.onClick()
    else
      assert(block,'Select a modifier group')
      local controls=tab.customModsBlockControls[req.id].controls
      if req.mode=='enabled' then controls.enableCheck.changeFunc(not block.enabled)
      elseif req.mode=='title' then assert(type(req.text)=='string'and #req.text<=240 and not req.text:find('%c'),'Invalid group name');controls.titleEdit.changeFunc(req.text)
      elseif req.mode=='text' then assert(type(req.text)=='string'and #req.text<=32768,'Modifier text is too long');controls.textEdit.changeFunc(req.text:gsub('\r\n','\n'))
      else error('Unknown modifier operation')end
    end
  elseif req.op=='character-rune' then
    local c=assert(build.itemsTab.runeSlots and build.itemsTab.runeSlots[req.slot],'Unknown rune slot')
    assert(c:IsShown(),'This build cannot socket runes on the character');index(req.value,c.list);c:SetSel(req.value)
  elseif req.op=='item-edit' then
    local value=item(req)
    if req.mode=='mod' then
      assert(build.itemsTab.ToggleDisplayItemModLine,'This engine cannot toggle item modifiers')
      build.itemsTab:ToggleDisplayItemModLine(index(req.id,modLines()).mod)
    else
      M.applyDisplayControl(req)
    end
    build.itemsTab:PopulateSlots();build.itemsTab:AddUndoState();build.buildFlag=true
  elseif req.op=='node-note' then
    assert(build.spec.nodeNotes and build.spec.nodes[req.id]and build.spec.nodes[req.id].alloc,'Select an allocated node')
    assert(type(req.text)=='string'and #req.text<=4096,'Node note is too long')
    build.spec.nodeNotes[req.id]=req.text~=''and req.text or nil;build.spec:AddUndoState();build.buildFlag=true
  else return false end
  return true
end
return M
