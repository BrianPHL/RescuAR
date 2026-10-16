local rules = require('evacuation-access')
local cases = require('evacuation-policy-cases')
for _, case in ipairs(cases) do
  local object = { get_value_by_key = function(self, key) return case.tags[key] end }
  if case.node then
    assert(rules.blocked_node(object) == case.blocked, case.name .. ': node barrier')
  else
    assert(rules.allowed(object, true) == case.forward, case.name .. ': forward')
    assert(rules.allowed(object, false) == case.backward, case.name .. ': backward')
    if case.factor then
      assert(math.abs(rules.factor(object) - case.factor) < 1e-9, case.name .. ': factor')
    end
  end
end
print(#cases .. ' shared Lua policy cases passed')
