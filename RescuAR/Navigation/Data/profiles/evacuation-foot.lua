-- Retain OSRM instructions; shared evacuation rules own access and cost.
local foot = dofile('/opt/foot.lua')
local rules = require('evacuation-access')
local stock_setup, stock_way = foot.setup, foot.process_way
foot.setup = function()
  local profile = stock_setup()
  profile.properties.weight_name = 'duration'
  profile.properties.traffic_light_penalty = 0
  profile.properties.u_turn_penalty = 0
  profile.properties.use_turn_restrictions = false
  profile.properties.call_tagless_node_function = true
  profile.access_tag_blacklist = {}
  profile.service_access_tag_blacklist = {}
  profile.oneway_handling = false
  profile.avoid = {}
  profile.speeds = { highway = {} }
  for _, highway in ipairs(rules.policy.allowedHighways) do
    profile.speeds.highway[highway] = rules.policy.walkingSpeedKmh
  end
  for _, highway in ipairs(rules.policy.explicitFootHighways) do
    profile.speeds.highway[highway] = rules.policy.walkingSpeedKmh
  end
  profile.surface_speeds = {}
  profile.route_speeds = {} -- A* has no ferry timetable/boarding support.
  return profile
end
foot.process_way = function(profile, way, result, ...)
  local forward, backward = rules.allowed(way, true), rules.allowed(way, false)
  if not forward and not backward then return end
  stock_way(profile, way, result, ...)
  local speed = rules.policy.walkingSpeedKmh / rules.factor(way)
  result.forward_mode = forward and mode.walking or mode.inaccessible
  result.backward_mode = backward and mode.walking or mode.inaccessible
  result.forward_speed = forward and speed or 0
  result.backward_speed = backward and speed or 0
  result.is_startpoint = true
end
foot.process_node = function(profile, node, result)
  result.barrier = rules.blocked_node(node)
  result.traffic_lights = rules.tag(node, 'highway') == 'traffic_signals'
end
return foot
