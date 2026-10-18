-- Desktop evaluator oracle. Does not load the widget's bridge or converters.
local stdout=io.stdout
local buildPath,casesPath=arg[1],arg[2]
print=function(...)io.stderr:write(tostring((...))..'\n')end
local json=require('dkjson')
dofile('HeadlessWrapper.lua')
local function read(path)local f=assert(io.open(path,'rb'));local s=f:read('*a');f:close();return s end
loadBuildFromXML(read(buildPath),'Modifier parity oracle')
for _=1,5 do
 local level=build.characterLevel
 if wipeGlobalCache then wipeGlobalCache()end
 build.buildFlag=true;runCallback('OnFrame')
 assert(not launch.promptMsg,'Oracle build failed')
 if level==build.characterLevel then break end
end
local cases=json.decode(read(casesPath));local result={}
LoadModule('Classes/TradeQueryGenerator')
for _,case in ipairs(cases)do
 local q=build.itemsTab.tradeQuery
 q.resultTbl={[1]={{item_string=case.raw}}};q.slotTables={[1]=build.itemsTab.slots[case.slot]}
 q.statSortSelectionList={{stat='Mana',weightMult=1}}
 if common.classes.TradeQueryGenerator.TradeQueryGenerator then
  q.tradeQueryGenerator=new('TradeQueryGenerator'):TradeQueryGenerator(q)
 else q.tradeQueryGenerator=new('TradeQueryGenerator',q)end
 local calc,base=build.calcsTab:GetMiscCalculator()
 local output
 local ev=q:GetResultEvaluation(1,1,function(args)output=calc(args);return output end,base)[1]
 result[#result+1]={score=ev.weight,mana=output.Mana}
end
stdout:write(json.encode(result)..'\n')
