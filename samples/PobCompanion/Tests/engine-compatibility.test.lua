-- Run with LuaJIT from the repository root; no account, engine or build needed.
local compatibility=dofile('samples/PobCompanion/Engine/engine-compatibility.lua')
local fields={'canCreateLoadout','skillWeaponSets','canEditNodeNotes','weaponTreeModes','timelessFinder'}
local function assertCapabilities(build,expected)
  local result=compatibility.capabilities(build)
  for _,key in ipairs(fields)do
    assert(type(result[key])=='boolean',key..' must be an explicit boolean')
    assert(result[key]==(expected[key]==true),key..' disagrees with the upstream shape')
  end
  local count=0;for _ in pairs(result)do count=count+1 end
  assert(count==#fields,'Capability schema changed; update the contract check')
  return result
end
local build={spec={},skillsTab={controls={}},treeTab={controls={}}}
assertCapabilities(build,{})
-- Legacy code/data alone does not mean the feature is available in desktop PoB.
build.treeTab.FindTimelessJewel=function()end
assertCapabilities(build,{})
build.treeTab.controls.findTimelessJewel={}
assertCapabilities(build,{timelessFinder=true})
build.CustomLoadout=function()end
build.skillsTab.controls.set1Enabled={}
build.spec.nodeNotes={}
build.spec.GetEffectiveAllocationPath=function()end
local old=assertCapabilities(build,{canCreateLoadout=true,skillWeaponSets=true,
  canEditNodeNotes=true,weaponTreeModes=true,timelessFinder=true})
-- Capability snapshots must follow a replacement/current build, never cached state.
build={spec={},skillsTab={controls={}},treeTab={controls={}}}
assertCapabilities(build,{})
assert(old.weaponTreeModes and old.timelessFinder,'New snapshots mutated earlier state')

common={classes={Tooltip={}}}
local constructorCalls=0
new=function(class,...)
  constructorCalls=constructorCalls+1
  return {class=class,arguments={...}}
end
assert(not compatibility.hasExplicitConstructors())
local implicit=compatibility.create('Example','first',nil,'third')
assert(constructorCalls==1 and implicit.class=='Example')
assert(implicit.arguments[1]=='first'and implicit.arguments[2]==nil and implicit.arguments[3]=='third')

common.classes.Tooltip.Tooltip=function()end
constructorCalls=0
new=function(class,...)
  assert(select('#',...)==0,'Explicit constructors cannot receive duplicate arguments through new')
  return {[class]=function(self,...)
    constructorCalls=constructorCalls+1;self.arguments={...}
  end}
end
assert(compatibility.hasExplicitConstructors())
local explicit=compatibility.create('Example','first',nil,'third')
assert(constructorCalls==1 and explicit.arguments[1]=='first'and explicit.arguments[3]=='third')
-- Incompatible required APIs must fail visibly instead of returning a half-initialized object.
new=function()return {}end
assert(not pcall(compatibility.create,'Example'))

for _,file in ipairs({'bridge','beta-compatibility','planning-features','item-authoring','timeless-tools','set-management','trade-search'})do
  assert(loadfile('samples/PobCompanion/Engine/'..file..'.lua'))
end
print('PASS capabilities: current-build shape, disabled legacy feature, strict booleans, constructor conventions and syntax')
