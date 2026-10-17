-- Feed public responses to PoB's own currency conversion implementation.
-- Network access stays in the managed adapter; no credentials enter Lua.
local M={}
function M.read(req)
  assert(req.realm=='pc'or req.realm=='poe2'or req.realm=='xbox'or req.realm=='sony','Choose a supported realm')
  assert(type(req.league)=='string'and #req.league<=160,'Choose a league')
  local owner=setmetatable({pbRealm=req.realm,pbLeague=req.league,pbCurrencyConversion={},controls={pbNotice={}}},{__index=build.itemsTab.tradeQuery})
  local notice
  owner.SetNotice=function(_,_,message)notice=message end
  local download=launch.DownloadPage
  launch.DownloadPage=function(_,url,callback)
    if url:match('^https://www%.pathofexile%.com/api/trade2?/data/static$')then callback({body=require('dkjson').encode(req.static)})
    elseif url:match('^https://web%.poecdn%.com/api/currency%-exchange')then callback({body=require('dkjson').encode(req.markets)})
    else error('Unexpected currency data request')end
  end
  local ok,err=pcall(owner.PullCXData,owner)
  launch.DownloadPage=download
  assert(ok,err)
  local rates=owner.pbCurrencyConversion[req.realm]and owner.pbCurrencyConversion[req.realm][req.league]
  assert(rates,notice or 'No exchange rates are available for this league')
  return {rates=rates}
end
function M.sort(req)
  assert(type(req.rows)=='table'and #req.rows<=100,'Choose up to 100 evaluated items')
  local mode=assert(({strength='Stat Value',value='Stat Value / Price',price='Price'})[req.mode],'Choose a ranking order')
  local rows={}
  for _,row in ipairs(req.rows)do
    assert(type(row.score)=='number'and row.score==row.score and math.abs(row.score)<math.huge,'Invalid evaluation score')
    if req.mode~='strength'then
      assert(type(row.amount)=='number'and row.amount>0 and row.amount<math.huge and type(row.currency)=='string','A listing has no usable fixed price')
      local rate=req.rates and req.rates[row.currency]
      assert(type(rate)=='number'and rate>0 and rate<math.huge,'No exchange rate for '..row.currency)
    end
    rows[#rows+1]={currency=row.currency,amount=row.amount,evaluation={{weight=row.score}}}
  end
  local owner=setmetatable({itemsTab=build.itemsTab,resultTbl={[1]=rows},GetResultEvaluation=function(_,_,index)return rows[index].evaluation end,
    pbRealm='rates',pbLeague='selected',pbCurrencyConversion={rates={selected=req.rates or{}}},
    sortModes={StatValue='Stat Value',StatValuePrice='Stat Value / Price',Price='Price'}},{__index=build.itemsTab.tradeQuery})
  local sorted,err=owner:SortFetchResults(1,mode)
  assert(sorted,err)
  local order={};for _,row in ipairs(sorted)do order[#order+1]=row.index end
  return {order=order}
end
return M
