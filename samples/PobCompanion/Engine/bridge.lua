-- Private, versioned adapter. Upstream PoB sources remain unmodified.
-- Only structured data enters this process; requests are never evaluated as Lua.
local output = io.stdout
local adapterDirectory=assert(arg[0]:match('^(.*[/\\])'),'Adapter requires an absolute path')
local treeGeometry=dofile(adapterDirectory..'tree-geometry.lua')
io.stdout:setvbuf('no')
print = function(...) -- Keep the protocol channel independent of upstream diagnostics.
  local parts = {}; for i=1,select('#',...) do parts[i]=tostring(select(i,...)) end
  io.stderr:write(table.concat(parts,'\t')..'\n')
end
local json = require('dkjson')
local originalRead = io.read
io.read = function() error('PoB startup requested an interactive error prompt') end
dofile('HeadlessWrapper.lua')
io.read = originalRead
assert(build, 'PoB initialization failed')
collectgarbage('setpause', 120)
collectgarbage('setstepmul', 200)
local function clean(s) return tostring(s or ''):gsub('%^x%x%x%x%x%x%x',''):gsub('%^%d','') end
local textRuns=dofile(adapterDirectory..'text-runs.lua')
local function finite(v) return type(v)=='number' and v==v and v~=math.huge and v~=-math.huge end
local configDefinitions=LoadModule('Modules/ConfigOptions')
local skillControlNames={'mainSkill','statSet','mainSkillPart','mainSkillMinion','mainSkillMinionSkill','mainSkillMinionSkillStatSet'}
local function configValue(d)
  local value=build.configTab.input[d.var]
  if value~=nil then return value end
  if d.type=='list' and type(d.list)=='table' then local option=d.list[d.defaultIndex or 1]; return type(option)=='table' and option.val or option end
  local t=d.type=='check' and 'boolean' or (d.type=='count' or d.type=='countAllowZero' or d.type=='integer' or d.type=='float') and 'number' or 'string'
  return build.configTab:GetDefaultState(d.var,t)
end
local function recalc()
  if wipeGlobalCache then wipeGlobalCache() end
  -- Desktop settles auto-level changes on its next frame. A headless reply must
  -- include the calculations for the level it reports, including after refunds.
  for _=1,4 do
    local level=build.characterLevel
    build.buildFlag=true
    runCallback('OnFrame')
    assert(not launch.promptMsg, 'PoB could not calculate this build')
    if level==build.characterLevel then return end
  end
  error('Automatic level did not settle; choose a manual level in desktop PoB')
end
local function choices(order, sets, active)
  local out={}; for _,id in ipairs(order or {}) do
    local set=sets[id]; out[#out+1]={id=id,title=clean(set.title or 'Default'),active=id==active}
  end; return out
end
local function itemDetails(item,slot)
  -- Reuse upstream presentation formatting, including rolled ranges and
  -- variant filtering. Never expose serialization-only prefix/suffix codes.
  local tooltip=new('Tooltip')
  local previous=build.itemsTab.showStatDifferences
  build.itemsTab.showStatDifferences=false
  local ok=pcall(build.itemsTab.AddItemTooltip,build.itemsTab,tooltip,item,slot,false,600)
  build.itemsTab.showStatDifferences=previous
  if not ok then return {{lines={{text='Item details could not be formatted by this engine.',tone='warning'}}}} end
  local groups,lines={},{}
  local function flush()if #lines>0 then groups[#groups+1]={lines=lines};lines={}end end
  for _,line in ipairs(tooltip.lines)do
    if line.text then
      local text=clean(line.text)
      if text~='' and not text:match('^Tip:') then
        local tone='text'
        for _,entry in ipairs({{'UNSUPPORTED','warning'},{'NEGATIVE','warning'},{'MAGIC','blue'},{'UNIQUE','orange'},{'RARE','gold'}})do
          local prefix=colorCodes[entry[1]]
          if prefix and line.text:sub(1,#prefix)==prefix then tone=entry[2];break end
        end
        lines[#lines+1]={text=text,tone=tone,runs=textRuns(line.text)}
      end
    else flush()end
  end
  flush();return groups
end
local function snapshot()
  local result={name=clean(build.buildName),level=build.characterLevel,treeVersion=build.spec.treeVersion,
    class=build.spec.curClassName,ascendancy=build.spec.curAscendClassName,autoLevel=build.characterLevelAutoMode==true,stats={},trees={},skills={},items={},conditions={},
    notes=clean(build.notesTab.controls.edit.buf),notesRuns=textRuns(build.notesTab.controls.edit.buf),warnings={},loadouts={},nodes={},links={},classes={},ascendancies={},slots={},skillOptions={},breakdowns={}}
  local active=build.calcsTab.mainEnv.player.mainSkill
  result.mainSkill=active and active.activeEffect and active.activeEffect.grantedEffect.name or ''
  result.mainSkillGroup=build.mainSocketGroup
  result.mainSkillDisabled=active and (active.disableReason~=nil or active.skillFlags and active.skillFlags.disable==true) or false
  result.mainSkillDisabledReason=active and clean(active.disableReason) or ''
  local flags=active and active.skillFlags or {}
  local types=active and active.skillTypes or {}
  result.mainSkillDamageKind=(flags.hit or flags.dot)and'damage'or active and active.minion and'minion'or types[SkillType.Buff]and'buff'or'unspecified'
  local description=active and active.activeEffect and active.activeEffect.grantedEffect.description or ''
  result.mainSkillDescription=clean(description);result.mainSkillDescriptionRuns=textRuns(description,true)
  result.skillOptionsGroup=build.mainSocketGroup
  local calcsSkill=build.calcsTab.calcsEnv.player.mainSkill
  result.breakdownContext={skill=calcsSkill and calcsSkill.activeEffect.grantedEffect.name or '',
    group=build.calcsTab.calcsEnv.mainSocketGroup,mode=build.calcsTab.input.misc_buffMode or ''}
  for _,name in ipairs(skillControlNames) do
    local control=build.controls[name]
    local ok,shown=pcall(function()return control and control:IsShown()end)
    if ok and shown and type(control.list)=='table' and #control.list>1 then
      local options={};for i,v in ipairs(control.list)do options[#options+1]={id=i,title=clean(type(v)=='table' and (v.label or v.name) or v),active=i==control.selIndex}end
      result.skillOptions[#result.skillOptions+1]={id=name,options=options}
    end
  end
  for id,c in pairs(build.spec.tree.classes) do result.classes[#result.classes+1]={id=id,title=clean(c.name),active=id==build.spec.curClassId} end
  table.sort(result.classes,function(a,b)return a.id<b.id end)
  for id,c in pairs(build.spec.curClass.classes) do result.ascendancies[#result.ascendancies+1]={id=id,title=clean(c.name),active=id==build.spec.curAscendClassId} end
  table.sort(result.ascendancies,function(a,b)return a.id<b.id end)
  for _,k in ipairs({'CombinedDPS','FullDPS','TotalDPS','Life','Mana','EnergyShield','Spirit','SpiritReserved','FireResist','ColdResist','LightningResist','ChaosResist','Evasion','Armour','EffectiveHitPool','PhysicalMaximumHitTaken','FireMaximumHitTaken','ColdMaximumHitTaken','LightningMaximumHitTaken','ChaosMaximumHitTaken'}) do
    local v=build.calcsTab.mainOutput[k]; if finite(v) then result.stats[#result.stats+1]={name=k,value=v} end
  end
  if build.calcsTab.mainOutput.Minion then
    for _,k in ipairs({'CombinedDPS','TotalDPS','Life','Speed','ActiveMinionLimit'}) do local v=build.calcsTab.mainOutput.Minion[k];if finite(v) then result.stats[#result.stats+1]={name='Minion'..k,value=v} end end
  end
  for _,stat in ipairs(result.stats)do
    local isMinion=stat.name:sub(1,6)=='Minion'
    local actor=isMinion and build.calcsTab.calcsEnv.minion or build.calcsTab.calcsEnv.player
    local key=isMinion and stat.name:sub(7) or stat.name
    local detail=actor and actor.breakdown and actor.breakdown[key]
    local lines,columns,rows={},{},{}
    if type(detail)=='table' then
      for _,line in ipairs(detail)do if type(line)=='string' then lines[#lines+1]=line end end
      -- Some upstream resource passes replace the base line but append the same
      -- multiplier/result sequence again. Collapse only exact complete repeats;
      -- never deduplicate individual modifiers, source rows, or distinct steps.
      if (key=='Life' or key=='Mana' or key=='Spirit') and #lines>=5 and
        clean(lines[1]):find('(base)',1,true) and clean(lines[#lines]):match('^= ') then
        local count=#lines-1
        for width=2,math.floor(count/2)do
          if count%width==0 then
            local repeated=true
            for i=width+2,#lines do if lines[i]~=lines[2+(i-2)%width]then repeated=false;break end end
            if repeated then for i=#lines,width+2,-1 do lines[i]=nil end;break end
          end
        end
      end
      if detail.slots then for name,value in pairs(detail.slots)do if type(value)=='number' or type(value)=='string'then lines[#lines+1]=name..': '..value end end end
      if detail.rowList and detail.colList then
        for _,column in ipairs(detail.colList)do columns[#columns+1]=clean(column.label or column.key)end
        for i,row in ipairs(detail.rowList)do
          if i>100 then break end
          local cells={};for _,column in ipairs(detail.colList)do
            local value=row[column.key];local text=(type(value)=='string'or type(value)=='number')and tostring(value)or'—'
            cells[#cells+1]={text=clean(text),runs=textRuns(text)}
          end
          rows[#rows+1]={cells=cells}
        end
      end
    end
    if #lines>0 or #rows>0 then local text=table.concat(lines,'\n');result.breakdowns[#result.breakdowns+1]={name=stat.name,text=clean(text),runs=textRuns(text),value=actor.output[key],columns=columns,rows=rows,rowCount=detail.rowList and #detail.rowList or 0}end
  end
  for _,line in ipairs(build.controls.warnings.lines or {}) do result.warnings[#result.warnings+1]=clean(line) end
  result.fullDpsIncludedGroups=0
  result.fullDpsContributions={}
  for _,skill in ipairs(build.calcsTab.mainOutput.SkillDPS or {})do
    if finite(skill.dps)then result.fullDpsContributions[#result.fullDpsContributions+1]={
      name=clean(skill.name),dps=skill.dps,count=skill.count or 1,source=clean(skill.source),trigger=clean(skill.trigger),part=clean(skill.skillPart)}end
  end
  for i,spec in ipairs(build.treeTab.specList) do result.trees[#result.trees+1]={id=i,title=clean(spec.title or 'Default'),active=i==build.treeTab.activeSpec} end
  result.itemSets=choices(build.itemsTab.itemSetOrderList,build.itemsTab.itemSets,build.itemsTab.activeItemSetId)
  result.skillSets=choices(build.skillsTab.skillSetOrderList,build.skillsTab.skillSets,build.skillsTab.activeSkillSetId)
  result.configSets=choices(build.configTab.configSetOrderList,build.configTab.configSets,build.configTab.activeConfigSetId)
  if build.SyncLoadouts then
    build:SyncLoadouts()
    for _,name in ipairs(build.controls.buildLoadouts.list or {}) do
      if type(name)=='string' and name~='No Loadouts' and not name:match('^%^7%^7') then
        local active=build.controls.buildLoadouts.list[build.controls.buildLoadouts.selIndex]==name
        -- Upstream's active-label lookup omits the version prefix on historical
        -- tree names. Derive that marker from the actual selected sets; do not
        -- change the upstream selection or calculation behavior.
        if not active and build.spec.treeVersion~=latestTreeVersion and build.treeTab:GetSpecList()[build.treeTab.activeSpec]==name then
          local title=build.spec.title or 'Default'
          local function matches(tab,order,sets,current)return #tab[order]==1 or (tab[sets][tab[current]].title or 'Default')==title end
          active=matches(build.itemsTab,'itemSetOrderList','itemSets','activeItemSetId') and matches(build.skillsTab,'skillSetOrderList','skillSets','activeSkillSetId') and matches(build.configTab,'configSetOrderList','configSets','activeConfigSetId')
        end
        result.loadouts[#result.loadouts+1]={id=#result.loadouts+1,title=clean(name),name=name,active=active}
      end
    end
  end
  for i,g in ipairs(build.skillsTab.socketGroupList) do
    local gems={}; for j,gem in ipairs(g.gemList) do
      gems[#gems+1]={id=j,name=clean(gem.nameSpec or (gem.gemData and gem.gemData.name)),level=gem.level,quality=gem.quality,enabled=gem.enabled~=false,count=gem.count or 1}
    end
    result.skills[#result.skills+1]={id=i,title=clean(g.label~='' and g.label or (gems[1] and gems[1].name) or 'Group'),gems=gems,enabled=g.enabled~=false,main=i==build.mainSocketGroup,fullDps=g.includeInFullDPS==true}
    if g.enabled~=false and g.includeInFullDPS then result.fullDpsIncludedGroups=result.fullDpsIncludedGroups+1 end
  end
  for slotIndex,slot in ipairs(build.itemsTab.orderedSlots) do
    result.slots[#result.slots+1]={name=slot.slotName}
    local item=build.itemsTab.items[slot.selItemId]
    if item then result.items[#result.items+1]={id=slotIndex,itemId=item.id,slot=slot.slotName or slot.id or '',name=clean(item.name or item.baseName),rarity=item.rarity,raw=item.raw or '',base=clean(item.baseName),details=itemDetails(item,slot)} end
  end
  local seen={}
  local configSection='General'
  for _,d in ipairs(configDefinitions) do
    if d.section then configSection=clean(d.section) end
    if d.var then
      local control=build.configTab.varControls[d.var]
      local ok,shown=pcall(function()return control and control:IsShown()end)
      local value=configValue(d)
      -- Imported settings remain visible even if current eligibility changes.
      if not seen[d.var] and value~=nil and (build.configTab.input[d.var]~=nil or (ok and shown)) then
        seen[d.var]=true
        local options={}; if type(d.list)=='table' then for _,o in ipairs(d.list) do if type(o)=='table' then options[#options+1]={label=clean(o.label),value=o.val} end end end
        local description=d.tooltip
        if type(description)=='function' then local valid,text=pcall(description,build.configTab.modList,build);description=valid and text or '' end
        result.conditions[#result.conditions+1]={id=d.var,title=clean(d.label or d.var),section=configSection,applicable=ok and shown==true,
          kind=d.type or 'text',value=value,options=options,description=type(description)=='string' and clean(description) or '',descriptionRuns=type(description)=='string' and textRuns(description) or {}}
      end
    end
  end
  if build.configTab.input.customMods then result.customMods=clean(build.configTab.input.customMods) end
  local passivePoints,ascendancyPoints=build.spec:CountAllocNodes()
  result.passivePoints=passivePoints;result.ascendancyPoints=ascendancyPoints
  for id,n in pairs(build.spec.nodes) do
    if finite(n.x) and finite(n.y) then
      local start=n.type=='ClassStart' or n.type=='AscendClassStart'
      local requiresChoice=n.type=='Mastery' or n.isMultipleChoiceOption or n.isSwitchable
      local path=n.path
      if path and build.spec.GetEffectiveAllocationPath then path=build.spec:GetEffectiveAllocationPath(n) end
      local canAllocate=not n.alloc and not start and path~=nil and not requiresChoice
      result.nodes[#result.nodes+1]={id=id,x=n.x,y=n.y,name=clean(n.dn),allocated=n.alloc==true,kind=n.type,
        description=clean(table.concat(n.sd or {},'\n')),descriptionRuns=textRuns(table.concat(n.sd or {},'\n')),icon=n.icon or '',distance=n.pathDist or 0,
        ascendancy=n.ascendancyName~=nil,ascendancyName=n.ascendancyName or '',canAllocate=canAllocate,canRefund=n.alloc==true and not start,requiresChoice=requiresChoice==true,
        pathCount=path and #path or 0,refundCount=n.depends and #n.depends or 0}
    end
  end
  result.links=treeGeometry(build.spec)
  table.sort(result.nodes,function(a,b)return a.id<b.id end)
  return result
end
local function validIndex(value,list)
  assert(type(value)=='number' and value%1==0 and list[value], 'Invalid selection'); return value
end
local function command(req)
  local op=req.op
  if op=='load' then
    assert(type(req.xml)=='string' and #req.xml<4*1024*1024,'Invalid build')
    launch.promptMsg=nil
    loadBuildFromXML(req.xml, req.name or 'Imported build')
    assert(not launch.promptMsg, 'Unsupported or invalid build')
  elseif op=='new' then newBuild()
  elseif op=='loadout' then
    assert(type(req.name)=='string' and #req.name<512,'Invalid loadout')
    local found; for i,name in ipairs(build.controls.buildLoadouts.list) do if name==req.name and not name:match('^%^7%^7') then found=i end end
    assert(found,'Loadout no longer exists'); build.controls.buildLoadouts:SetSel(found)
  elseif op=='set' then
    if req.kind=='tree' then build.treeTab:SetActiveSpec(validIndex(req.id,build.treeTab.specList))
    elseif req.kind=='items' then build.itemsTab:SetActiveItemSet(validIndex(req.id,build.itemsTab.itemSets))
    elseif req.kind=='skills' then build.skillsTab:SetActiveSkillSet(validIndex(req.id,build.skillsTab.skillSets))
    elseif req.kind=='config' then build.configTab:SetActiveConfigSet(validIndex(req.id,build.configTab.configSets))
    else error('Invalid set type') end
  elseif op=='node' then
    local n=build.spec.nodes[validIndex(req.id,build.spec.nodes)]
    assert(n.type~='ClassStart' and n.type~='AscendClassStart','Starting nodes cannot be changed')
    assert(req.mode=='allocate' or req.mode=='refund','Choose allocation or refund')
    if req.mode=='refund' then
      assert(n.alloc,'This passive is not allocated')
      build.spec:DeallocNode(n)
      assert(not n.alloc,'PoB could not refund this passive')
    else
      assert(not n.alloc,'This passive is already allocated')
      assert(n.path,'There is no allocation path from this build')
      assert(n.type~='Mastery' and not n.isMultipleChoiceOption and not n.isSwitchable,
        'This passive requires an additional choice. Set it in desktop PoB before importing.')
      build.spec:AllocNode(n)
      assert(n.alloc,'PoB could not allocate this path in the selected tree mode')
    end
  elseif op=='skill' then
    local g=build.skillsTab.socketGroupList[validIndex(req.id,build.skillsTab.socketGroupList)]
    if req.field=='main' then build.mainSocketGroup=req.id
    elseif req.field=='enabled' then g.enabled=not g.enabled
    elseif req.field=='fullDps' then g.includeInFullDPS=not g.includeInFullDPS
    else error('Invalid skill field') end
  elseif op=='gem' then
    local g=build.skillsTab.socketGroupList[validIndex(req.group,build.skillsTab.socketGroupList)]
    local gem=g.gemList[validIndex(req.id,g.gemList)]
    assert(req.field=='level' or req.field=='quality' or req.field=='count','Invalid gem field')
    assert(finite(req.value) and req.value>=0 and req.value<=1000,'Invalid gem value'); gem[req.field]=req.value
    build.skillsTab:ProcessSocketGroup(g)
  elseif op=='inspect-skill' then
    local group=validIndex(req.group,build.skillsTab.socketGroupList)
    local saved=build:SaveDB('code')
    build.mainSocketGroup=group; recalc()
    local inspected=snapshot()
    -- The upstream selector callbacks are tied to the calculation group. Use a
    -- temporary calculation context, then restore the exact authored build.
    loadBuildFromXML(saved,'Working build');recalc()
    return {state=inspected,xml=saved}
  elseif op=='skill-option' then
    local group=validIndex(req.group,build.skillsTab.socketGroupList)
    local mainGroup=build.mainSocketGroup
    build.mainSocketGroup=group;recalc()
    local allowed=false;for _,name in ipairs(skillControlNames)do if name==req.control then allowed=true end end
    assert(allowed,'Invalid skill option');local control=build.controls[req.control]
    assert(control and control:IsShown(),'Skill option no longer applies')
    validIndex(req.value,control.list);control:SetSel(req.value)
    build.mainSocketGroup=mainGroup
  elseif op=='config' then
    assert(type(req.key)=='string','Unknown condition')
    local definition;for _,d in ipairs(configDefinitions)do if d.var==req.key then definition=d;break end end
    assert(definition,'Unknown condition');local old=configValue(definition)
    assert(type(old)==type(req.value),'Invalid condition type')
    if type(req.value)=='string' then assert(#req.value<=32768,'Condition too long') end
    build.configTab.input[req.key]=req.value; build.configTab:BuildModList()
  elseif op=='item' then
    assert(type(req.raw)=='string' and #req.raw<=32768,'Invalid item text')
    build.itemsTab:CreateDisplayItemFromRaw(req.raw)
    assert(build.itemsTab.displayItem and build.itemsTab.displayItem.base,'Item was not recognized')
    assert(type(req.slot)=='string' and build.itemsTab.slots[req.slot],'Select an equipment slot')
    local item=build.itemsTab.displayItem
    assert(build.itemsTab:IsItemValidForSlot(item,req.slot),'This item cannot be equipped in that slot')
    build.itemsTab:AddDisplayItem(true)
    build.itemsTab.slots[req.slot]:SetSelItemId(item.id)
    build.itemsTab:PopulateSlots()
  elseif op=='level' then
    assert(finite(req.value) and req.value%1==0 and req.value>=1 and req.value<=100,'Invalid level')
    build.characterLevel=req.value; build.characterLevelAutoMode=false
  elseif op=='class' then
    validIndex(req.id,build.spec.tree.classes);build.spec:SelectClass(req.id)
  elseif op=='ascendancy' then
    validIndex(req.id,build.spec.curClass.classes);build.spec:SelectAscendClass(req.id)
  elseif op~='snapshot' then error('Unknown operation') end
  recalc()
  return {state=snapshot(),xml=build:SaveDB('code')}
end
output:write(json.encode({ready=true,version=launch.versionNumber})..'\n')
for line in io.lines() do
  if #line>6*1024*1024 then error('Request too large') end
  local req,_,err=json.decode(line,1,nil)
  if not req or type(req)~='table' or err then output:write(json.encode({ok=false,error='Invalid request'})..'\n')
  else
    local before=build:SaveDB('code')
    local ok,result=pcall(command,req)
    if not ok then
      launch.promptMsg=nil
      local restored=pcall(loadBuildFromXML,before,'Recovered build')
      if not restored or launch.promptMsg then error('Engine recovery failed') end
    end
    output:write(json.encode(ok and {id=req.id,ok=true,result=result} or {id=req.id,ok=false,error=clean(result):sub(1,600)})..'\n')
    before=nil; result=nil; req=nil
    -- Desktop PoB uses a generous allocation threshold. A sleeping worker must
    -- finish collecting transient calculation/snapshot graphs before it idles.
    collectgarbage('collect')
  end
end
