-- Keep section membership, visibility, formatting and source attribution upstream.
local M={}
local function actor()local tab=build.calcsTab;return tab.input.showMinion and tab.calcsEnv.minion or tab.calcsEnv.player end
local function visit(action)
  local tab=build.calcsTab;local current=actor()
  for s,section in ipairs(tab.sectionList)do if tab:CheckFlag(section)then
    for u,sub in ipairs(section.subSection)do if tab:CheckFlag(sub.data)then
      local headings={}
      for r,row in ipairs(sub.data)do if tab:CheckFlag(row)then
        for c,cell in ipairs(row)do if cell.format and tab:CheckFlag(cell)then
          local value=formatCalcStr(cell.format,current,cell)
          if not row.label and #cell==0 then headings[c]=value
          elseif value~=''then action(s,u,r,c,section,sub,row,cell,value,headings[c])end
        end end
      end end
    end end
  end end
end
local function outputs(cell)
  local names={}
  for key in cell.format:gmatch('output:([%a%.:]+)')do names[#names+1]=key end
  for _,part in ipairs(cell)do if part.breakdown then names[#names+1]=part.breakdown end end
  return names
end
local function skillControls()
  for _,section in ipairs(build.calcsTab.sectionList)do
    if section.id=='SkillSelect'then
      build:RefreshSkillSelectControls(section.controls,build.calcsTab.input.skill_number,'Calcs')
      return section.controls
    end
  end
  error('PoB calculation skill controls are unavailable')
end
function M.catalog(clean,textRuns)
  local sections,byId={},{}
  visit(function(s,u,r,c,section,sub,row,cell,value,heading)
    local key=s..'.'..u;local target=byId[key]
    if not target then target={id=key,title=clean(sub.label),items={}};byId[key]=target;sections[#sections+1]=target end
    local title=clean(row.label or sub.label)
    if heading and heading~=''then title=title..' · '..clean(heading):gsub(':$','')end
    local outputNames=outputs(cell)
    target.items[#target.items+1]={id=key..'.'..r..'.'..c,title=title,value=clean(value),runs=textRuns(value),outputs=outputNames,hasBreakdown=#cell>0}
  end)
  local a=actor()
  local tab=build.calcsTab;local group=build.skillsTab.socketGroupList[tab.input.skill_number]
  local skills={}
  for i,entry in ipairs(skillControls().mainSkill.list or{})do skills[#skills+1]={id=i,title=clean(entry.label or tostring(i)),active=i==(group and group.mainActiveSkillCalcs or 1)}end
  return{sections=sections,context={skill=a.mainSkill.activeEffect.grantedEffect.name,group=tab.input.skill_number,skills=skills,
    mode=tab.input.misc_buffMode or'',minion=tab.input.showMinion==true,hasMinion=tab.calcsEnv.minion~=nil}}
end
function M.detail(req,clean,textRuns)
  assert(type(req.id)=='string'and #req.id<=48,'Choose a calculation')
  local found
  visit(function(s,u,r,c,section,sub,row,cell,value,heading)
    if s..'.'..u..'.'..r..'.'..c==req.id then found=cell end
  end)
  assert(found,'This calculation is not available in the current skill context')
  local control=build.calcsTab.controls.breakdown
  control:SetBreakdownData(nil);control:SetBreakdownData(found,false)
  local result={sections={},rowCount=0}
  for _,section in ipairs(control.sectionList or{})do
    local target={title=clean(section.label or''),kind=section.type,lines={},columns={},rows={},footer=clean(section.footer or'')}
    if section.type=='TEXT'then
      for _,text in ipairs(section.lines)do if type(text)=='string'then target.lines[#target.lines+1]={text=clean(text),runs=textRuns(text)}end end
    elseif section.type=='TABLE'then
      for _,column in ipairs(section.colList)do target.columns[#target.columns+1]=clean(column.label or column.key)end
      for _,row in ipairs(section.rowList)do
        local cells={}
        for _,column in ipairs(section.colList)do
          local value=row[column.key];local text=(type(value)=='string'or type(value)=='number')and tostring(value)or''
          cells[#cells+1]={text=clean(text),runs=textRuns(text)}
        end
        target.rows[#target.rows+1]={cells=cells}
      end
      result.rowCount=result.rowCount+#target.rows
    elseif section.type=='RADIUS'then
      local text='Radius: '..tostring(section.radius);target.lines={{text=text,runs=textRuns(text)}}
    end
    result.sections[#result.sections+1]=target
  end
  return result
end
-- Inspection overrides are temporary: never replace the main skill or rewrite
-- the build's separately saved desktop Calcs selections.
function M.read(req,clean,textRuns,recalc,restore)
  local saved=build:SaveDB('code')
  if req.context==nil then
    return{state=req.op=='calculation-catalog'and M.catalog(clean,textRuns)or M.detail(req,clean,textRuns),xml=saved}
  end
  local context=req.context
  assert(type(context)=='table','Invalid calculation context')
  local ok,value=pcall(function()
    local groupId=context.group==0 and build.mainSocketGroup or context.group
    assert(type(groupId)=='number'and groupId%1==0 and (build.skillsTab.socketGroupList[groupId]or(context.group==0 and #build.skillsTab.socketGroupList==0)),'Choose an available skill group')
    assert(({EFFECTIVE=true,COMBAT=true,BUFFED=true,UNBUFFED=true})[context.mode],'Choose an available buff mode')
    assert(type(context.minion)=='boolean','Choose player or minion calculations')
    local group=build.skillsTab.socketGroupList[groupId]
    local skill=context.skill or 0
    assert(type(skill)=='number'and skill%1==0 and skill>=0,'Choose an active skill')
    if group then group.mainActiveSkillCalcs=skill>0 and skill or group.mainActiveSkill or 1 end
    for _,gem in ipairs(group and group.gemList or{})do
      for _,field in ipairs({'statSet','skillPart','skillStageCount','skillMineCount','skillMinion','skillMinionItemSet','skillMinionSkill','skillMinionSkillStatSetIndexLookup'})do
        gem[field..'Calcs']=type(gem[field])=='table'and copyTable(gem[field])or gem[field]
      end
    end
    build.calcsTab.input.skill_number=groupId
    build.calcsTab.input.misc_buffMode=context.mode
    build.calcsTab.input.showMinion=context.minion
    recalc()
    assert(not context.minion or build.calcsTab.calcsEnv.minion,'This skill has no minion calculations')
    assert(skill==0 or skill<=#(skillControls().mainSkill.list or{}),'This active skill is no longer available')
    return req.op=='calculation-catalog'and M.catalog(clean,textRuns)or M.detail(req,clean,textRuns)
  end)
  restore(saved,'Working build');recalc()
  assert(ok,value)
  return{state=value,xml=saved}
end

return M
