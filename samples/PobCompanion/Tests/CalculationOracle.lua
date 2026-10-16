-- Independent upstream calculation: never loads the widget adapter.
local output=io.stdout
local inputPath=arg[1]
print=function(...)io.stderr:write(tostring((...))..'\n')end
local json=require('dkjson')
dofile('HeadlessWrapper.lua')
local file=assert(io.open(inputPath,'rb'));local xml=file:read('*a');file:close()
loadBuildFromXML(xml,'Guide workflow oracle')
for _=1,6 do
  if wipeGlobalCache then wipeGlobalCache()end
  build.buildFlag=true;runCallback('OnFrame')
  assert(not launch.promptMsg,'Oracle build failed')
end
local result={level=build.characterLevel,stats={},showAverage=build.calcsTab.mainEnv.player.mainSkill.skillData.showAverage and true or false}
for _,key in ipairs({'CombinedDPS','TotalDPS','FullDPS','Life','Mana','EnergyShield','Armour','Evasion','FireResist','ColdResist','LightningResist','ChaosResist','EffectiveHitPool'})do
  result.stats[key]=build.calcsTab.mainOutput[key]
end
output:write(json.encode(result)..'\n')
