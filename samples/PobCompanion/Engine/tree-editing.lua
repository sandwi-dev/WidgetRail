-- Expose upstream tree editing dialogs without reimplementing their modifiers.
local M={}
function M.canTattoo(node)
  if not build.spec.tree.tattoo or not build.treeTab.ModifyNodePopup or node.expansionSkill then return false end
  return node.isTattoo or node.type=='Keystone'or node.type=='Normal'and(node.dn=='Strength'or node.dn=='Dexterity'or node.dn=='Intelligence')or
    node.type=='Notable'and node.sd and node.sd[1]and(node.sd[1]:match('%+30 to Dexterity')or node.sd[1]:match('%+30 to Strength')or node.sd[1]:match('%+30 to Intelligence'))~=nil
end
function M.tattoo(req,clean,textRuns)
  local node=assert(build.spec.nodes[req.id],'Choose a passive')
  assert(M.canTattoo(node),'PoB does not offer tattoos for this passive')
  local open,close,popups=main.OpenPopup,main.ClosePopup,main.popups
  local controls
  main.popups={{}}
  main.OpenPopup=function(_,_,_,_,c)controls=c;return main.popups[1]end
  main.ClosePopup=function()end
  local ok,output=pcall(function()
    build.treeTab:ModifyNodePopup(node)
    assert(controls and controls.modSelect,'PoB provides no tattoo choices here')
    if req.remove then
      assert(node.isTattoo,'This passive has no tattoo');controls.reset.onClick();return {}
    end
    if req.choice then
      local selected
      for i,v in ipairs(controls.modSelect.list)do if i==req.choice and(not req.key or req.key==v.id)then selected=i end end
      assert(selected,'This tattoo is no longer eligible for this passive')
      controls.modSelect.selIndex=selected;controls.save.onClick();return {}
    end
    local choices={}
    for i,v in ipairs(controls.modSelect.list)do
      local description=table.concat(v.descriptions or{},'\n')
      choices[#choices+1]={id=i,key=v.id,title=clean(v.label):match('^(.-)%s%s+')or clean(v.label),description=clean(description),runs=textRuns(description)}
    end
    return {choices=choices}
  end)
  main.OpenPopup,main.ClosePopup,main.popups=open,close,popups
  assert(ok,output);return output
end
function M.mode(value)
  assert(build.spec.GetEffectiveAllocationPath,'This engine does not provide weapon-set passive allocation')
  assert(value==0 or value==1 or value==2,'Choose shared, weapon set 1 or weapon set 2')
  build.spec.allocMode=value
end
return M
