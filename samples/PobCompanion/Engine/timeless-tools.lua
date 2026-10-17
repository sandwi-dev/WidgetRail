-- Use PoB's finder controls, search and item constructor. No seed calculations here.
local M={}
local resultsVersion=0
local resultRows,resultSocket={},{}
local resultControl
function M.range(req,clean)
  assert(req.version==resultsVersion,'Jewel results expired. Search again.')
  assert(type(req.start)=='number'and req.start>=0 and req.start%1==0 and type(req.count)=='number'and req.count>=0 and req.count<=1000 and req.count%1==0,'Invalid jewel result range')
  local rows={}
  for i=req.start+1,math.min(#resultRows,req.start+req.count)do local row=resultRows[i]
    rows[#rows+1]={id=i,seed=row.seed,score=row.total,socket=row.socketId or resultSocket.id,title=clean(row.label),socketLabel=clean(row.socketLabel or resultSocket.label or'')}
  end
  return {results=rows}
end
local selections={'jewelSelect','conquerorSelect','socketSelect','devotionSelect1','devotionSelect2','fallbackWeightsList','abyssAscendancySelect','nodeSelect'}
local labels={jewelSelect='Jewel type',conquerorSelect='Conqueror',socketSelect='Jewel socket',devotionSelect1='First devotion modifier',devotionSelect2='Second devotion modifier',fallbackWeightsList='Fallback weights',abyssAscendancySelect='Ascendancy outcome',nodeSelect='Desired modifier'}
function M.inspect(req,clean,textRuns)
  assert(req.version==resultsVersion and type(req.id)=='number'and resultRows[req.id]and resultControl,'Jewel results expired. Search again.')
  local tooltip=new('Tooltip');if common.classes.Tooltip.Tooltip then tooltip:Tooltip()end
  resultControl:AddValueTooltip(tooltip,req.id,resultRows[req.id])
  local lines={}
  for _,line in ipairs(tooltip.lines)do
    if line.text and not line.text:find('Double click to add this jewel',1,true)then lines[#lines+1]={text=clean(line.text),runs=textRuns(line.text)}end
  end
  return {lines=lines}
end
-- PoE2 retains legacy finder code and item bases, but disables its desktop control.
function M.available()return build.treeTab.FindTimelessJewel~=nil and build.treeTab.controls.findTimelessJewel~=nil end
local function shown(c)
  return c and c:IsShown()
end
function M.run(req,clean,textRuns)
  assert(M.available(),'This engine has no Timeless Jewel finder')
  local open,close,popups,download=main.OpenPopup,main.ClosePopup,main.popups,launch.DownloadPage
  local controls
  main.popups={{}}
  main.OpenPopup=function(_,_,_,_,c)controls=c;return main.popups[1]end
  main.ClosePopup=function()end
  -- Finder search is local. League fetching belongs to the separate trade workflow.
  launch.DownloadPage=function(_,_,callback)callback(nil,'Use the widget trade search to browse listings')end
  local ok,result=pcall(function()
    build.treeTab:FindTimelessJewel()
    assert(controls and controls.searchButton,'PoB could not open its jewel finder')
    if req.fallback then controls.searchListFallback.shown=true;controls.searchList.shown=false end
    if req.control then
      local permitted=false;for _,key in ipairs(selections)do if key==req.control then permitted=true end end
      assert(permitted,'Unknown jewel setting');local c=assert(controls[req.control],'This jewel setting is unavailable')
      assert(shown(c)and type(req.value)=='number'and req.value%1==0 and c.list[req.value],'Choose an available option')
      c:SetSel(req.value)
    elseif req.weight then
      local names={primary='nodeSlider',secondary='nodeSlider2',minimum='nodeSlider3'}
      local key=assert(names[req.weight],'Unknown modifier weight')
      assert(type(req.modifier)=='number'and req.modifier>1 and controls.nodeSelect.list[req.modifier],'Choose a modifier first')
      controls.nodeSelect:SetSel(req.modifier)
      local maximum=req.weight=='minimum'and 500 or 10
      assert(type(req.value)=='number'and req.value>=0 and req.value<=maximum,'Modifier weight is out of range')
      controls[key]:SetVal(req.value/maximum)
    elseif req.field then
      assert(req.field=='searchList'or req.field=='searchListFallback','Unknown desired-modifier list')
      assert(type(req.text)=='string'and #req.text<=8192,'Modifier list is too long')
      controls[req.field]:SetText(req.text,true)
    elseif req.filter~=nil then
      assert(type(req.filter)=='boolean','Choose whether to restrict to allocated nodes')
      controls.socketFilter.state=req.filter;controls.socketFilter.changeFunc(req.filter)
    end
    if req.control or req.weight or req.field or req.filter~=nil then wipeTable(build.timelessData.searchResults)end
    if req.search or req.addSeed then
      if req.addSeed then
        assert(type(req.addSeed)=='number'and type(req.socket)=='number','Choose a jewel result')
      end
      controls.searchButton.onClick()
      if req.addSeed then
        local selected
        for i,row in ipairs(build.timelessData.searchResults)do
          local socket=row.socketId or build.timelessData.jewelSocket.id
          if row.seed==req.addSeed and socket==req.socket then selected=i;break end
        end
        assert(selected,'This result no longer matches the finder settings. Search again.')
        controls.searchResults:OnSelClick(selected,build.timelessData.searchResults[selected],true)
      end
    end
    local settings={}
    for _,key in ipairs(selections)do local c=controls[key]
      if shown(c)and c.list and #c.list>0 then
        local options={}
        for i,v in ipairs(c.list)do
          local title=type(v)=='table'and(v.label or v.name or v.keystone)or v
          local desc=type(v)=='table'and table.concat(v.descriptions or{},'\n')or''
          options[#options+1]={id=i,title=clean(title or tostring(i)),description=clean(desc),runs=textRuns(desc),active=i==c.selIndex}
        end
        settings[#settings+1]={key=key,title=labels[key],options=options}
      end
    end
    resultControl=controls.searchResults;resultsVersion=resultsVersion+1;resultRows=build.timelessData.searchResults or{};resultSocket=build.timelessData.jewelSocket
    local results=M.range({version=resultsVersion,start=0,count=64},clean).results
    return {editingFallback=controls.searchListFallback and controls.searchListFallback.shown==true,resultCount=#resultRows,resultVersion=resultsVersion,modifier=controls.nodeSelect.selIndex or 1,primary=(controls.nodeSlider.val or .1)*10,secondary=(controls.nodeSlider2.val or .1)*10,minimum=(controls.nodeSlider3.val or 0)*500,secondaryEnabled=controls.nodeSlider2:IsEnabled(),settings=settings,results=results,searchList=controls.searchList.buf or'',fallback=controls.searchListFallback and controls.searchListFallback.buf or'',filter=controls.socketFilter.state==true}
  end)
  main.OpenPopup,main.ClosePopup,main.popups,launch.DownloadPage=open,close,popups,download
  assert(ok,result);return result
end
return M
