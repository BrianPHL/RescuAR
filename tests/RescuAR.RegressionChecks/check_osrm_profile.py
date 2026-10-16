"""Run shared cases and the actual OSRM foot wrapper using an optional Lua runtime.

Usage: python check_osrm_profile.py --stock-profiles PATH --runtime PATH
The runtime and official OSRM profiles may live in ignored obj test output.
Docker runs the shared Lua cases with its own Lua interpreter at build time.
"""
import argparse
import json
import runpy
import sys
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--stock-profiles", type=Path, required=True)
    parser.add_argument("--runtime", type=Path, required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(args.runtime.resolve()))
    from lupa.lua52 import LuaRuntime

    repo = Path(__file__).resolve().parents[2]
    data = repo / "RescuAR/Navigation/Data"
    profiles = data / "profiles"
    output = Path(__file__).resolve().parent / "obj/policy-test"
    old_args = sys.argv
    sys.argv = ["build-evacuation-policy.py", str(data / "evacuation-policy.json"),
                str(output), "--cases", str(profiles / "evacuation-policy-cases.json")]
    runpy.run_path(str(profiles / "build-evacuation-policy.py"), run_name="__main__")
    sys.argv = old_args
    runtime = LuaRuntime(unpack_returned_tuples=True)
    package_path = ";".join(str(path.resolve()).replace("\\", "/") + "/?.lua"
                            for path in (profiles, output, args.stock_profiles))
    runtime.execute("package.path = " + json.dumps(package_path) + " .. ';' .. package.path")
    runtime.execute((profiles / "test-evacuation-policy.lua").read_text())
    # These enums/functions are supplied by OSRM's extractor in production.
    runtime.execute("""
        mode = {inaccessible=0, walking=1, ferry=2}
        road_priority_class = {motorway=0,trunk=1,primary=2,secondary=3,tertiary=4,
          unclassified=5,residential=6,service=7,other=8}
        canonicalizeStringList = function(value) return value end
        local native_dofile = dofile
        dofile = function(path)
          if path == '/opt/foot.lua' then return native_dofile(stock_foot_path) end
          return native_dofile(path)
        end
    """)
    runtime.globals().stock_foot_path = str((args.stock_profiles / "foot.lua").resolve()).replace("\\", "/")
    runtime.globals().evacuation_profile_path = str((profiles / "evacuation-foot.lua").resolve()).replace("\\", "/")
    runtime.execute("""
        local profile_module = dofile(evacuation_profile_path)
        local profile = profile_module.setup()
        local rules = require('evacuation-access')
        local cases = require('evacuation-policy-cases')
        for _, case in ipairs(cases) do
          local object = {get_value_by_key=function(self,key) return case.tags[key] end}
          if case.node then
            local result = {}
            profile_module.process_node(profile,object,result)
            assert(result.barrier == case.blocked,case.name .. ': actual profile node')
          else
            local result = {forward_mode=0,backward_mode=0,forward_speed=-1,backward_speed=-1,
              duration=0,road_classification={}}
            profile_module.process_way(profile,object,result)
            assert((result.forward_mode == mode.walking) == case.forward,case.name .. ': actual profile forward')
            assert((result.backward_mode == mode.walking) == case.backward,case.name .. ': actual profile backward')
            if case.factor and case.forward then
              assert(math.abs(result.forward_speed - rules.policy.walkingSpeedKmh/case.factor) < 1e-9,
                case.name .. ': actual profile weighted speed')
            end
            if case.factor and case.backward then
              assert(math.abs(result.backward_speed - rules.policy.walkingSpeedKmh/case.factor) < 1e-9,
                case.name .. ': actual profile backward speed')
            end
          end
        end
        assert(profile.properties.traffic_light_penalty == 0 and profile.properties.u_turn_penalty == 0)
        print(#cases .. ' actual OSRM v5.27.1 profile cases passed (Lua 5.2)')
    """)


if __name__ == "__main__":
    main()
