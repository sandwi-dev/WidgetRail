-- Independent desktop comparison oracle: does not load the widget adapter.
local stdout=io.stdout
local beforePath,afterPath=arg[1],arg[2]
print=function(...)io.stderr:write(tostring((...))..'\n')end
local json=require('dkjson')
dofile('HeadlessWrapper.lua')
local function load(path)
  local file=assert(io.open(path,'rb'));local xml=file:read('*a');file:close()
  loadBuildFromXML(xml,'Comparison oracle')
  for _=1,5 do
    local level=build.characterLevel
    if wipeGlobalCache then wipeGlobalCache()end
    build.buildFlag=true;runCallback('OnFrame')
    assert(not launch.promptMsg,'Oracle build failed')
    if level==build.characterLevel then return end
  end
  error('Oracle auto-level did not settle')
end
load(beforePath);local before=copyTable(build.calcsTab.mainOutput,true)
load(afterPath)
local tooltip=new('Tooltip')
if common.classes.Tooltip.Tooltip then tooltip:Tooltip() end
build:AddStatComparesToTooltip(tooltip,before,build.calcsTab.mainOutput,'')
local lines={}
for _,line in ipairs(tooltip.lines)do
  if line.text then
    local text=line.text:gsub('%^x%x%x%x%x%x%x',''):gsub('%^%d','')
    if text~=''and text~='Minion:'and text~='\nMinion:'and text~='\nPlayer:'and text~='Player:'then lines[#lines+1]=text end
  end
end
stdout:write(json.encode(lines)..'\n')
