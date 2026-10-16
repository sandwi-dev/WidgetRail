-- Preserve upstream inline formatting as palette roles, never desktop RGB.
-- Unknown colours return to normal text; colour alone does not invent meaning.
local roles={NORMAL='text',MAGIC='blue',RARE='gold',UNIQUE='orange',RELIC='green',GEM='green',GEMINFO='muted',
  PROPHECY='violet',CURRENCY='gold',CRAFTED='blue',ENCHANTED='blue',CUSTOM='green',SOURCE='blue',
  UNSUPPORTED='warning',WARNING='orange',TIP='muted',FIRE='orange',COLD='blue',LIGHTNING='gold',CHAOS='violet',
  POSITIVE='green',NEGATIVE='warning',HIGHLIGHT='warning',OFFENCE='orange',DEFENCE='blue'}
local codes={}
for name,role in pairs(roles)do if colorCodes[name]then codes[colorCodes[name]:lower()]=role end end
return function(value)
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
  return runs
end
