-- Invoke the same set-list callbacks used by desktop PoB. No set schema is recreated here.
local M={}
local types={tree={'treeTab','PassiveSpecListControl','specList'},items={'itemsTab','ItemSetListControl','itemSetOrderList','itemSets'},
  skills={'skillsTab','SkillSetListControl','skillSetOrderList','skillSets'},config={'configTab','ConfigSetListControl','configSetOrderList','configSets'}}
local function create(class,...)
  if common.classes.Tooltip.Tooltip then local object=new(class);object[class](object,...);return object end
  return new(class,...)
end
function M.edit(req)
  local info=assert(types[req.kind],'Unknown set type');local tab=build[info[1]]
  local list=tab[info[3]];local sets=info[4]and tab[info[4]]or list
  local position
  for i,value in ipairs(list)do if(req.kind=='tree'and i or value)==req.id then position=i end end
  assert(position,'This set no longer exists')
  local mode=req.mode;assert(mode=='new'or mode=='copy'or mode=='rename'or mode=='delete','Unknown set action')
  if mode=='delete'then assert(#list>1,'Keep at least one set of each type')
  else
    assert(type(req.name)=='string'and #req.name<=400,'Invalid set name')
    req.name=req.name:match('^%s*(.-)%s*$')
    assert(#req.name>0 and require('lua-utf8').len(req.name)<=100 and not req.name:find('[%c%^{}]'),'Use a plain name of 1 to 100 characters')
    for id,set in pairs(sets)do assert(mode=='rename'and id==req.id or(set.title or'Default'):lower()~=req.name:lower(),'A set of this type already has that name')end
    if mode~='rename'then assert(#list<128,'This build already has 128 sets of this type')end
  end
  local control=create(info[2],nil,{0,0,350,200},tab)
  control.selIndex=position;control.selValue=list[position]
  local popup,confirm,close=main.OpenPopup,main.OpenConfirmPopup,main.ClosePopup
  local completed=false
  main.OpenPopup=function(_,width,height,title,controls)
    assert(mode~='delete'and controls.edit and controls.save,'Unexpected upstream set dialog')
    controls.edit:SetText(req.name)
    if controls.edit.changeFunc then controls.edit.changeFunc(req.name)end
    controls.save.onClick();completed=true
  end
  main.OpenConfirmPopup=function(_,title,message,button,callback)
    assert(mode=='delete','Unexpected upstream confirmation');callback();completed=true
  end
  main.ClosePopup=function()end
  local ok,err=pcall(function()control.controls[mode].onClick()end)
  main.OpenPopup,main.OpenConfirmPopup,main.ClosePopup=popup,confirm,close
  assert(ok,err);assert(completed,'Upstream did not complete this set action')
  local selected=mode=='delete'and(req.kind=='tree'and math.min(position,#list)or list[math.min(position,#list)])or
    req.kind=='tree'and control.selIndex or control.selValue
  if mode~='delete'then for id,set in pairs(sets)do if set.title==req.name then selected=id;break end end end
  build.buildFlag=true
  return selected
end
return M
