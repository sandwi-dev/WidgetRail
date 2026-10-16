-- Presentation adapter for the pinned PoB PassiveTree:BuildConnector rules.
-- The engine's connector list is authoritative: logical adjacency also contains
-- virtual links (for example class/ascendancy roots) that must not be drawn.
local function geometry(spec)
  local result, seen = {}, {}
  local imageScale=spec.tree.scaleImage or 1 -- PoE 1 uses unscaled tree coordinates.
  local function append(connector)
    local a,b=spec.nodes[connector.nodeId1],spec.nodes[connector.nodeId2]
    if not a or not b or not a.x or not b.x or a.id==b.id then return end
    local key=math.min(a.id,b.id)..':'..math.max(a.id,b.id)
    if seen[key] then return end
    seen[key]=true
    local edge={a=a.id,b=b.id}
    if connector.type~='LineConnector' then
      local orbit
      for _,connection in pairs(a.connections or {}) do
        if connection.id==b.id then orbit=connection.orbit;break end
      end
      local cx,cy,r
      if orbit and orbit~=0 then
        r=spec.tree.orbitRadii[math.abs(orbit)+1]*imageScale
        local dx,dy=b.x-a.x,b.y-a.y
        local distance=math.sqrt(dx*dx+dy*dy)
        if distance>0 and distance<r*2 then
          local perpendicular=math.sqrt(r*r-distance*distance/4)*(orbit>0 and 1 or -1)
          cx=a.x+dx/2+perpendicular*dy/distance
          cy=a.y+dy/2-perpendicular*dx/distance
        end
      elseif a.group and a.g==b.g and a.o==b.o then
        cx=a.group.x*imageScale;cy=a.group.y*imageScale
        r=spec.tree.orbitRadii[a.o+1]*imageScale
      end
      if cx and r and r>0 then
        local start=math.atan2(a.y-cy,a.x-cx)
        local finish=math.atan2(b.y-cy,b.x-cx)
        local sweep=(finish-start)%(2*math.pi)
        if sweep>math.pi then sweep=sweep-2*math.pi end
        edge.cx=cx;edge.cy=cy;edge.r=r;edge.start=start;edge.sweep=sweep
      end
    end
    result[#result+1]=edge
  end
  for _,connector in pairs(spec.tree.connectors or {})do append(connector)end
  for _,subGraph in pairs(spec.subGraphs or {})do
    for _,connector in pairs(subGraph.connectors or {})do append(connector)end
  end
  table.sort(result,function(a,b)return a.a==b.a and a.b<b.b or a.a<b.a end)
  return result
end
return geometry
