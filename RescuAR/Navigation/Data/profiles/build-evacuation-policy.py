"""Convert the canonical JSON policy and regression cases to Lua (stdlib only)."""
import argparse
import json
import math
from pathlib import Path


def lua(value):
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        if not math.isfinite(value):
            raise ValueError("Non-finite routing policy number")
        return repr(value)
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=False)
    if isinstance(value, list):
        return "{" + ",".join(lua(item) for item in value) + "}"
    if isinstance(value, dict):
        return "{" + ",".join("[" + lua(key) + "]=" + lua(item) for key, item in value.items()) + "}"
    raise ValueError("Unsupported routing policy value")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("policy", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--cases", type=Path)
    args = parser.parse_args()
    policy = json.loads(args.policy.read_text(encoding="utf-8-sig"))
    for key in ("privateFactor", "majorRoadFactor", "unmarkedCrossingFactor"):
        if policy[key] < 1:
            raise ValueError(f"{key} must preserve the distance lower bound")
    if any(value < 1 for value in policy["surfaceFactors"].values()):
        raise ValueError("Surface factors must be at least one")
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "evacuation-policy.lua").write_text("return " + lua(policy) + "\n", encoding="utf-8")
    if args.cases:
        cases = json.loads(args.cases.read_text(encoding="utf-8-sig"))
        (args.output / "evacuation-policy-cases.lua").write_text("return " + lua(cases) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
