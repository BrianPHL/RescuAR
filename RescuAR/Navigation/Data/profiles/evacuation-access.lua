-- Generated policy comes from the JSON embedded in the mobile application.
local policy = require('evacuation-policy')
local access = { policy = policy }
local function has(values, value)
  for _, candidate in ipairs(values) do if candidate == value then return true end end
  return false
end
local function tag(object, key)
  local value = object:get_value_by_key(key)
  return value and tostring(value):match('^%s*(.-)%s*$'):lower() or ''
end
access.tag = tag
function access.denied(object, direction)
  local foot = direction and tag(object, 'foot:' .. direction) or ''
  if foot == '' then foot = tag(object, 'foot') end
  if has(policy.deniedFoot, foot) then return true end
  if has(policy.allowedFoot, foot) then return false end
  local general = direction and tag(object, 'access:' .. direction) or ''
  if general == '' then general = tag(object, 'access') end
  return has(policy.deniedAccess, general)
end
function access.blocked_node(object)
  return access.denied(object) or tag(object, 'impassable') == 'yes' or
    tag(object, 'status') == 'impassable' or has(policy.hardBarriers, tag(object, 'barrier')) or
    (has(policy.gates, tag(object, 'barrier')) and tag(object, 'locked') == 'yes') or
    tag(object, 'crossing') == 'no'
end
local function active(object, key)
  local value = tag(object, key)
  return value ~= '' and value ~= 'no' and value ~= 'false'
end
function access.allowed(object, forward)
  local highway = tag(object, 'highway')
  local direction = forward and 'forward' or 'backward'
  local foot = tag(object, 'foot:' .. direction)
  if foot == '' then foot = tag(object, 'foot') end
  local explicit = has(policy.allowedFoot, foot)
  if (not has(policy.allowedHighways, highway) and
      not (has(policy.explicitFootHighways, highway) and explicit)) or
      access.denied(object, direction) or tag(object, 'impassable') == 'yes' or
      tag(object, 'status') == 'impassable' or tag(object, 'area') == 'yes' or
      has(policy.hardBarriers, tag(object, 'barrier')) or
      (has(policy.gates, tag(object, 'barrier')) and tag(object, 'locked') == 'yes') or
      active(object, 'construction') or active(object, 'proposed') or
      (tag(object, 'footway') == 'crossing' and tag(object, 'crossing') == 'no') or
      (tag(object, 'motorroad') == 'yes' and not explicit) or
      (highway == 'service' and tag(object, 'tunnel') == 'building_passage' and not explicit) then
    return false
  end
  local oneway = tag(object, 'oneway:foot')
  if forward then return oneway ~= '-1' end
  return oneway ~= 'yes' and oneway ~= '1' and oneway ~= 'true'
end
function access.factor(object)
  local factor = 1
  if tag(object, 'access') == 'private' then factor = factor * policy.privateFactor end
  if has(policy.majorHighways, tag(object, 'highway')) then factor = factor * policy.majorRoadFactor end
  if tag(object, 'footway') == 'crossing' and
      not has(policy.markedCrossings, tag(object, 'crossing')) and
      tag(object, 'crossing:markings') ~= 'yes' and tag(object, 'crossing:markings') ~= 'zebra' then
    factor = factor * policy.unmarkedCrossingFactor
  end
  return factor * (policy.surfaceFactors[tag(object, 'surface')] or 1)
end
return access
