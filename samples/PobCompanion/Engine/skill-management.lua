-- Use the pinned desktop editor's callbacks and defaults; calculations remain upstream.
local utf8=require('lua-utf8')
local function index(value,list)
  assert(type(value)=='number' and value%1==0 and list[value],'Invalid skill selection')
  return value
end
local function display(group)
  build.skillsTab:SetDisplayGroup(group)
  build.skillsTab:UpdateGemSlots()
end
local extraGemControls={{'enabled','Gem enabled'},{'corruptLevel','Corruption'},{'enableGlobal1'},{'enableGlobal2'}}
local function gemControls(req,clean)
  local group=build.skillsTab.socketGroupList[index(req.group,build.skillsTab.socketGroupList)]
  local gem=group.gemList[index(req.gem,group.gemList)];display(group)
  local slot=build.skillsTab.gemSlots[req.gem];local result={name=clean(gem.nameSpec or''),controls={}}
  for _,entry in ipairs(extraGemControls)do
    local control=slot[entry[1]]
    if control and control:IsShown()then
      local item={id=entry[1],title=clean(entry[2]or control:GetProperty('label')),enabled=control:IsEnabled()==true}
      if control.list then
        item.options={}
        for i,option in ipairs(control.list)do item.options[#item.options+1]={id=i,title=clean(type(option)=='table'and(option.label or option.name)or option),active=i==control.selIndex}end
      else item.value=control.state==true end
      result.controls[#result.controls+1]=item
    end
  end
  return result
end
local function catalog(group,gemIndex,sort)
  display(group)
  local tab=build.skillsTab
  local picker=tab.gemSlots[gemIndex].nameSpec
  picker:PopulateGemList()
  local result,seen,keys={},{},{}
  for key,gem in pairs(picker.gems)do
    if not seen[gem.id] and not gem.grantedEffect.hideFromGemList and not gem.grantedEffect.hideFromSideBar and
      (not picker.FilterSupport or picker:FilterSupport(gem.id,gem)) then
      seen[gem.id]=true
      keys[gem.id]=key
      assert(gem.tagString==nil or type(gem.tagString)=='string','Invalid upstream gem tag text')
      assert(gem.tags==nil or type(gem.tags)=='table','Invalid upstream gem tags')
      local tags={}
      for tag,enabled in pairs(gem.tags or {})do
        assert(type(tag)=='string' and type(enabled)=='boolean','Invalid upstream gem tag')
        if enabled then tags[#tags+1]=tag end
      end
      table.sort(tags)
      result[#result+1]={id=gem.id,name=gem.name,support=gem.grantedEffect.support==true,
        tagString=gem.tagString or '',searchTags=table.concat(tags,', '),
        description=type(gem.grantedEffect.description)=='string' and gem.grantedEffect.description or ''}
    end
  end
  table.sort(result,function(a,b)return a.name==b.name and a.id<b.id or a.name<b.name end)
  if sort and sort~='name' then
    assert(picker.DPSBuilder,'This engine does not expose lazy gem comparisons')
    local allowed=false
    for _,choice in ipairs(tab.controls.sortGemsByDPSFieldControl.list)do if choice.type==sort then allowed=true end end
    assert(allowed,'Unknown comparison statistic')
    local oldSort,oldField=tab.sortGemsByDPS,tab.sortGemsByDPSField
    tab.sortGemsByDPS=true;tab.sortGemsByDPSField=sort
    local ok,err=pcall(function()
      picker:UpdateSortCache()
      picker:BuildList('')
      local worker=coroutine.create(function()picker:DPSBuilder()end)
      while coroutine.status(worker)~='dead'do local success,message=coroutine.resume(worker);assert(success,message)end
      for _,gem in ipairs(result)do
        gem.comparison=picker.sortCache.dps[keys[gem.id]] or 0
        gem.canSupport=picker.sortCache.canSupport[keys[gem.id]]==true
      end
      table.sort(result,function(a,b)
        if a.canSupport~=b.canSupport then return a.canSupport end
        if a.comparison~=b.comparison then return a.comparison>b.comparison end
        return a.name==b.name and a.id<b.id or a.name<b.name
      end)
    end)
    tab.sortGemsByDPS=oldSort;tab.sortGemsByDPSField=oldField
    assert(ok,err)
  end
  return result
end
return {
  gemControls=gemControls,
  copy=function(req)
    local groups=build.skillsTab.socketGroupList
    local group=groups[index(req.group,groups)]
    assert(not group.source,'Granted skill groups cannot be copied')
    local old,text=Copy,nil
    Copy=function(value)text=value end
    local ok,err=pcall(build.skillsTab.CopySocketGroup,build.skillsTab,group)
    Copy=old
    assert(ok,err);assert(type(text)=='string' and #text<=32768,'Group clipboard text is too large')
    return text
  end,
  catalog=function(req)
    local groups=build.skillsTab.socketGroupList
    local group=groups[index(req.group,groups)]
    assert(not group.source,'Granted skills cannot have their gems replaced')
    assert(type(req.gem)=='number' and req.gem%1==0 and req.gem>=1 and req.gem<=#group.gemList+1,'Invalid gem slot')
    return catalog(group,req.gem,req.sort)
  end,
  edit=function(req)
    local tab=build.skillsTab
    local groups=tab.socketGroupList
    local group=req.group and groups[index(req.group,groups)]
    local main=groups[build.mainSocketGroup]
    local calcs=groups[build.calcsTab.input.skill_number or 1]
    if req.op=='skill-group' then
      if req.mode=='create' then
        assert(#groups<256,'This skill set already contains 256 groups')
        tab.controls.groupList.controls.new.onClick()
        group=tab.displayGroup
      elseif req.mode=='paste' then
        assert(#groups<256 and type(req.text)=='string' and #req.text<=32768,'Invalid group clipboard text')
        local count=#groups
        local old=Paste;Paste=function()return req.text end
        local ok,err=pcall(tab.PasteSocketGroup,tab)
        Paste=old;assert(ok,err)
        assert(#groups==count+1,'No skill group was found in the clipboard. Copy a socket group from desktop PoB.')
        group=groups[#groups]
        local expected=0
        for line in req.text:gmatch('[^\r\n]+')do
          if line:match('%S') and not line:match('^Label: ') and not line:match('^Slot: ') and not line:match('^Weapon Set: ')then expected=expected+1 end
        end
        assert(#group.gemList==expected and expected<=64,'Some clipboard gems could not be imported. Paste a full build export instead.')
        for _,gem in ipairs(group.gemList)do assert(not gem.errMsg and (gem.gemData or gem.grantedEffect),'A clipboard gem is unavailable in this engine')end
      elseif req.mode=='delete-all' then
        for i=#groups,1,-1 do if not groups[i].source then table.remove(groups,i)end end
        group=groups[1];display(group)
      elseif req.mode=='duplicate' then
        assert(group and not group.source,'Granted skill groups cannot be duplicated')
        assert(#groups<256,'This skill set already contains 256 groups')
        local copy=copyTable(group,true);copy.gemList={}
        for i,gem in ipairs(group.gemList)do copy.gemList[i]=copyTable(gem,true)end
        copy.label=utf8.sub(group.label~='' and group.label or group.displayLabel or 'Group',1,43)..' (copy)'
        table.insert(groups,req.group+1,copy);group=copy;display(group)
      elseif req.mode=='delete' then
        assert(group and not group.source,'Granted skill groups cannot be deleted; remove their source instead')
        table.remove(groups,req.group)
        group=groups[math.min(req.group,#groups)];display(group)
      elseif req.mode=='move' then
        assert(group and (req.direction==-1 or req.direction==1),'Invalid move')
        local target=req.group+req.direction
        assert(target>=1 and target<=#groups,'Group is already at the end of the list')
        table.remove(groups,req.group);table.insert(groups,target,group)
        tab.controls.groupList:OnOrderChange(req.group,target)
      elseif req.mode=='rename' then
        assert(group and type(req.name)=='string' and #req.name<=200 and utf8.len(req.name)<=50 and not req.name:find('%c'),'Invalid group label')
        display(group);tab.controls.groupLabel.changeFunc(req.name)
      elseif req.mode=='count' then
        assert(group and group.source,'Group count applies to granted groups')
        assert(type(req.value)=='number' and req.value==req.value and req.value>=0 and req.value<=1000,'Invalid group count')
        group.groupCount=req.value
      elseif req.mode=='slot' then
        assert(group and not group.source,'A granted skill uses its source equipment slot')
        assert(tab.controls.groupSlot,'This engine uses per-skill weapon sets')
        display(group)
        local matched=false
        for i,choice in ipairs(tab.controls.groupSlot.list)do
          if (choice.slotName or '')==req.slot then tab.controls.groupSlot:SetSel(i);matched=true;break end
        end
        assert(matched,'Invalid equipment slot')
      elseif req.mode=='weapon-set' then
        assert(group and (req.set==1 or req.set==2),'Invalid weapon set')
        display(group)
        local control=tab.controls['set'..req.set..'Enabled']
        assert(control and control:IsEnabled(),'This weapon set is controlled by the skill or its equipment')
        control.changeFunc(not group['set'..req.set])
      else error('Unknown skill group operation')end
    elseif req.op=='gem-control'then
      assert(group,'Missing skill group');index(req.gem,group.gemList);display(group)
      local allowed=false;for _,entry in ipairs(extraGemControls)do if entry[1]==req.control then allowed=true end end
      assert(allowed,'Unknown gem control')
      local control=tab.gemSlots[req.gem][req.control]
      assert(control and control:IsShown()and control:IsEnabled(),'This gem option is unavailable or controlled by its source')
      if control.list then control:SetSel(index(req.value,control.list))
      else assert(type(req.value)=='boolean','Choose enabled or disabled');control.changeFunc(req.value)end
      tab:ProcessSocketGroup(group)
    elseif req.op=='gem-manage' then
      assert(group,'Missing skill group');display(group)
      if req.mode=='add' or req.mode=='replace' then
        assert(not group.source,'Granted skills cannot have their gems replaced')
        assert(type(req.gemId)=='string' and build.data.gems[req.gemId],'Unknown gem')
        local gemIndex=req.mode=='add' and #group.gemList+1 or index(req.gem,group.gemList)
        assert(gemIndex<=64,'This group already contains 64 gems')
        local allowed=false
        for _,entry in ipairs(catalog(group,gemIndex))do if entry.id==req.gemId then allowed=true;break end end
        assert(allowed,'This gem is not available in the current engine settings')
        tab.gemSlots[gemIndex].nameSpec.gemChangeFunc(req.gemId,true,false,true)
      elseif req.mode=='delete' then
        assert(not group.source,'Granted skills cannot have their gems removed')
        tab.gemSlots[index(req.gem,group.gemList)].delete.onClick()
      elseif req.mode=='enabled' then
        local gem=group.gemList[index(req.gem,group.gemList)];gem.enabled=not gem.enabled
      else error('Unknown gem operation')end
      tab:ProcessSocketGroup(group)
    end
    -- Preserve the actual calculation groups when indexes shift around them.
    local function locate(value,fallback)
      for i,g in ipairs(groups)do if g==value then return i end end
      return math.max(1,math.min(fallback or 1,#groups))
    end
    build.mainSocketGroup=locate(main,build.mainSocketGroup)
    build.calcsTab.input.skill_number=locate(calcs,build.calcsTab.input.skill_number)
    tab:AddUndoState();build.buildFlag=true
    return group
  end,
}
