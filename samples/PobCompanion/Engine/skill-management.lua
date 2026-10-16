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
local function catalog(group,gemIndex)
  display(group)
  local tab=build.skillsTab
  local picker=tab.gemSlots[gemIndex].nameSpec
  picker:PopulateGemList()
  local result,seen={},{}
  for _,gem in pairs(picker.gems)do
    if not seen[gem.id] and not gem.grantedEffect.hideFromGemList and not gem.grantedEffect.hideFromSideBar and
      (not picker.FilterSupport or picker:FilterSupport(gem.id,gem)) then
      seen[gem.id]=true
      result[#result+1]={id=gem.id,name=gem.name,support=gem.grantedEffect.support==true,
        description=type(gem.grantedEffect.description)=='string' and gem.grantedEffect.description or ''}
    end
  end
  table.sort(result,function(a,b)return a.name==b.name and a.id<b.id or a.name<b.name end)
  return result
end
return {
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
    return catalog(group,req.gem)
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
          if line:match('%S') and not line:match('^Label: ') and not line:match('^Slot: ')then expected=expected+1 end
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
        display(group)
        local matched=false
        for i,choice in ipairs(tab.controls.groupSlot.list)do
          if (choice.slotName or '')==req.slot then tab.controls.groupSlot:SetSel(i);matched=true;break end
        end
        assert(matched,'Invalid equipment slot')
      else error('Unknown skill group operation')end
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
