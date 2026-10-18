-- Pure metadata parity fixtures: no game network, UI or native dependencies.
local present=dofile(arg[1]..'/tree-presentation.lua')
local background=dofile(arg[1]..'/tree-artwork.lua')
local node={id=1,x=10,y=20,type='Socket',group={},overlay={alloc='JewelFrameAllocated',unalloc='JewelFrameUnallocated'},alloc=true}
local item={title='Example',baseName='Sapphire',jewelRadiusIndex=1}
local tree={scaleImage=1,classes={[1]={background={image='ClassesTest',x=0,y=0,width=1500,height=1500},classes={}}},ascendNameMap={},groups={},keystoneMap={}}
local build={spec={tree=tree,nodes={[1]=node},curClassId=1,curAscendClassId=0},treeTab={viewer={}},calcsTab={mainEnv={grantedPassives={}}},itemsTab={GetSocketAndJewelForNodeID=function()return {},item end},data={jewelRadius={{outer=1200,inner=600}},gameConstants={PassiveTreeJewelDistanceMultiplier=2}}}
local p=present(build,node)
assert(p.art.frame=='JewelFrameAllocated'and p.art.socket=='Sapphire')
assert(p.art.radii[1].outer==2400 and p.art.radii[1].inner==1200 and p.art.radii[1].x==10)
node.alloc=false;p=present(build,node);assert(p.art.socket==''and #p.art.radii==0 and p.art.frame=='JewelFrameUnallocated')
assert(getmetatable(p.art.radii).__jsontype=='array','Empty radii serialize as an array')
node.alloc=true;item.jewelData={fromNothingKeystones={Example=true}};tree.keystoneMap.Example={x=100,y=200}
p=present(build,node);assert(p.art.radii[1].x==100 and p.art.radii[1].y==200 and p.art.radii[1].inner==150)
local b=background(build);assert(#b==1 and b[1].image=='ClassesTest'and b[1].width==1500)
tree.classes[1].classes[2]={background={image='ClassesAsc'}};build.spec.curAscendClassId=2
b=background(build);assert(b[1].image=='ClassesAsc')
tree.groups[1]={x=100,y=200,background={image='Group',isHalfImage=false}}
b=background(build);assert(#b==2 and b[2].half and b[2].x==100,'PoE2 mirrors upstream non-nil half-image flag')
tree.groups[1].isProxy=true;assert(#background(build)==1)
tree.scaleImage=nil;tree.classes={};tree.groups={};build.spec.curClassId=1
build.treeTab.viewer.GetJewelSocketOverlay=function(_,jewel,expansion)return expansion and 'AltSocket'or 'Socket' end
item.jewelData={};node.expansionJewel={size=1};node.overlay.allocAlt='JewelSocketAltActive'
p=present(build,node);assert(p.art.frame=='JewelSocketAltActive'and p.art.socket=='AltSocket'and #p.art.radii==0)
node.expansionJewel=nil;p=present(build,node);assert(p.art.radii[1].outer==1200,'PoE1 does not use PoE2 radius scale')
b=background(build);assert(b[1].image=='BackgroundStr'and b[1].x==-2750 and b[1].scale==1.33)
print('PASS upstream frame state, occupied sockets, empty/refunded sockets, game radius scales, alternate radius centers, class/ascendancy backgrounds, half images and proxy exclusion')
