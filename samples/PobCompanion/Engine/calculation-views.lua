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
function M.catalog(clean,textRuns)
  local sections,byId={},{}
  visit(function(s,u,r,c,section,sub,row,cell,value,heading)
    local key=s..'.'..u;local target=byId[key]
    if not target then target={id=key,title=clean(sub.label),items={}};byId[key]=target;sections[#sections+1]=target end
    local title=clean(row.label or sub.label)
    if heading and heading~=''then title=title..' · '..clean(heading):gsub(':$','')end
    local outputs={};for key in cell.format:gmatch('output:([%a%.:]+)')do outputs[#outputs+1]=key end
    for _,part in ipairs(cell)do if part.breakdown then outputs[#outputs+1]=part.breakdown end end
    target.items[#target.items+1]={id=key..'.'..r..'.'..c,title=title,value=clean(value),runs=textRuns(value),outputs=outputs,hasBreakdown=#cell>0}
  end)
  local a=actor()
  return{sections=sections,context={skill=a.mainSkill.activeEffect.grantedEffect.name,mode=build.calcsTab.input.misc_buffMode or'',minion=build.calcsTab.input.showMinion==true}}
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
return M
