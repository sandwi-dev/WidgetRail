-- Preserve upstream inline formatting as palette roles, never desktop RGB.
-- Unknown colours return to normal text; colour alone does not invent meaning.
local roles={NORMAL='text',MAGIC='blue',RARE='gold',UNIQUE='orange',RELIC='green',GEM='green',GEMINFO='muted',
  PROPHECY='violet',CURRENCY='gold',CRAFTED='blue',ENCHANTED='blue',CUSTOM='green',SOURCE='blue',
  UNSUPPORTED='warning',WARNING='orange',TIP='muted',FIRE='orange',COLD='blue',LIGHTNING='gold',CHAOS='violet',
  POSITIVE='green',NEGATIVE='warning',HIGHLIGHT='warning',OFFENCE='orange',DEFENCE='blue'}
local codes={}
for name,role in pairs(roles)do if colorCodes[name]then codes[colorCodes[name]:lower()]=role end end
-- Skill descriptions reference other granted effects, including hidden effects.
-- Use upstream names rather than maintaining a second list of skill names.
-- Single-word names are ambiguous in prose (for example, "Frenzy Charge").
local skillNamesByInitial
local function highlightReferences(runs)
  if not skillNamesByInitial then
    skillNamesByInitial={}
    local seen={}
    for _,skill in pairs(data.skills)do
      local name=skill.name
      if type(name)=='string' and name:find(' ',1,true) and not seen[name] then
        seen[name]=true
        local initial=name:sub(1,1)
        skillNamesByInitial[initial]=skillNamesByInitial[initial]or{}
        table.insert(skillNamesByInitial[initial],name)
      end
    end
    for _,names in pairs(skillNamesByInitial)do table.sort(names,function(a,b)return #a==#b and a<b or #a>#b end)end
  end
  local result={}
  for _,run in ipairs(runs)do
    if run.tone~='text' then result[#result+1]=run
    else
      local text,pos,start=run.text,1,1
      while pos<=#text do
        local found
        if pos==1 or not text:sub(pos-1,pos-1):match('[%w_]')then
          for _,name in ipairs(skillNamesByInitial[text:sub(pos,pos)]or{})do
            if text:sub(pos,pos+#name-1)==name and not text:sub(pos+#name,pos+#name):match('[%w_]')then found=name;break end
          end
        end
        if found then
          if start<pos then result[#result+1]={text=text:sub(start,pos-1),tone='text'}end
          result[#result+1]={text=found,tone='violet'};pos=pos+#found;start=pos
        else pos=pos+1 end
      end
      if start<=#text then result[#result+1]={text=text:sub(start),tone='text'}end
    end
  end
  return result
end
return function(value,skillReferences)
  local text=tostring(value or '')
  local runs,pos,tone={},1,'text'
  local function append(s)
    if s==''then return end
    local previous=runs[#runs]
    if previous and previous.tone==tone then previous.text=previous.text..s
    else runs[#runs+1]={text=s,tone=tone}end
  end
  while pos<=#text do
    local a,b=text:find('%^x%x%x%x%x%x%x',pos)
    local c,d=text:find('%^%d',pos)
    if c and (not a or c<a)then a,b=c,d end
    if not a then append(text:sub(pos));break end
    append(text:sub(pos,a-1));tone=codes[text:sub(a,b):lower()]or'text';pos=b+1
  end
  return skillReferences and highlightReferences(runs)or runs
end
