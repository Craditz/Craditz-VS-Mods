"""Generate Core's cradle body anchors from owned base shapes.

The two carry clips rotate LowerTorso. Shape origins differ substantially, so
Carried's size-ratio profiles alone cannot align the visible body with the
carrier's arms. This computes each shape's volume-weighted visible center
after that rotation, plus the carrier-relative position of the reviewed fox
pose. Runtime placement uses that fixed point instead of retaining each
passenger's different Carried seat height.
The inputs are owned Animalica pack assets; never edit their generated shapes.
"""

from __future__ import annotations

import json
import math
from pathlib import Path


WORK = Path(__file__).resolve().parents[2]
OUTPUT = WORK / "core/assets/animalicacore/config/animalica/carried-body-centers.json"
PACKS = ("canids", "bears", "hooved", "small-odd", "enemies")
STYLES = {"animalica-cradle": 0, "animalica-babycradle": 180}
REFERENCE = "feralfox:feralfox"
# Lower both shared body targets below the already reviewed fox seat. The
# fox itself keeps Carried's original placement in the runtime bridge.
SHARED_CRADLE_DROP = 0.16


def multiply(left: list[list[float]], right: list[list[float]]) -> list[list[float]]:
    return [[sum(left[row][n] * right[n][column] for n in range(4))
             for column in range(4)] for row in range(4)]


def identity() -> list[list[float]]:
    return [[float(row == column) for column in range(4)] for row in range(4)]


def translate(x: float, y: float, z: float) -> list[list[float]]:
    result = identity()
    result[0][3], result[1][3], result[2][3] = x, y, z
    return result


def scale(x: float, y: float, z: float) -> list[list[float]]:
    result = identity()
    result[0][0], result[1][1], result[2][2] = x, y, z
    return result


def rotate_xyz(x: float, y: float, z: float) -> list[list[float]]:
    # Matches Vintage Story Mat4f.RotateByXYZ, used by ShapeElement version 0.
    sx, sy, sz = (math.sin(math.radians(value)) for value in (x, y, z))
    cx, cy, cz = (math.cos(math.radians(value)) for value in (x, y, z))
    xy, xz = sx * sy, -cx * sy
    return [
        [cy * cz, -cy * sz, sy, 0],
        [xy * cz + cx * sz, cx * cz - xy * sz, -sx * cy, 0],
        [xz * cz + sx * sz, sx * cz - xz * sz, cx * cy, 0],
        [0, 0, 0, 1],
    ]


def transform(matrix: list[list[float]], point: list[float]) -> list[float]:
    return [sum(matrix[row][column] * point[column] for column in range(3))
            + matrix[row][3] for row in range(3)]


def visible_center(shape: dict, roll: int) -> list[float]:
    weighted = [0.0, 0.0, 0.0]
    total = 0.0
    torso_count = 0

    def visit(element: dict, parent: list[list[float]]) -> None:
        nonlocal total, torso_count
        origin = element.get("rotationOrigin", [0, 0, 0])
        start = element.get("from", [0, 0, 0])
        end = element.get("to", start)
        is_torso = element.get("name") == "LowerTorso"
        if is_torso:
            torso_count += 1
        animation = (0, -90, roll) if is_torso else (0, 0, 0)
        angles = [element.get("rotation" + axis, 0) + delta
                  for axis, delta in zip("XYZ", animation)]
        local = multiply(
            translate(*(value / 16 for value in origin)),
            multiply(
                rotate_xyz(*angles),
                multiply(
                    scale(*(element.get("scale" + axis, 1) for axis in "XYZ")),
                    translate(*((start[axis] - origin[axis]) / 16 for axis in range(3))),
                ),
            ),
        )
        matrix = multiply(parent, local)
        dimensions = [(end[axis] - start[axis]) / 16 for axis in range(3)]
        volume = math.prod(max(0, value) for value in dimensions)
        if element.get("faces") and volume > 0:
            point = transform(matrix, [value / 2 for value in dimensions])
            for axis in range(3):
                weighted[axis] += point[axis] * volume
            total += volume
        for child in element.get("children", []):
            visit(child, matrix)

    for element in shape["elements"]:
        visit(element, identity())
    if torso_count != 1 or total <= 0:
        raise ValueError(f"Shape needs one LowerTorso and visible volume; got {torso_count}, {total}")
    return [value / total for value in weighted]


def generate() -> None:
    centers: dict[str, dict[str, list[float]]] = {}
    fox_eye_height = None
    for pack in PACKS:
        assets = WORK / pack / "assets"
        for config_path in sorted(assets.glob("*/config/customplayermodels/*.json")):
            models = json.loads(config_path.read_text(encoding="utf-8-sig"))
            for code, model in models.items():
                if not isinstance(model, dict) or not str(model.get("Group", "")).lower().startswith("animalica"):
                    continue
                domain, shape_code = model["ShapePath"].split(":", 1)
                shape_path = assets / domain / "shapes" / (shape_code + ".json")
                shape = json.loads(shape_path.read_text(encoding="utf-8-sig"))
                key = f"{config_path.parent.parent.parent.name}:{code}"
                if key in centers:
                    raise ValueError(f"Duplicate model code {key}")
                centers[key] = {style: visible_center(shape, roll)
                                for style, roll in STYLES.items()}
                if key == REFERENCE:
                    fox_eye_height = float(model["EyeHeight"])

    if len(centers) != 67 or REFERENCE not in centers or fox_eye_height is None:
        raise ValueError(f"Expected 67 owned models and {REFERENCE}; got {len(centers)}")

    # The reviewed fox at normal size uses the largest Carried bridal profile.
    # Match CarryOffset.Axis: anchor*carrier - (anchor-seat)*passenger.
    style_path = WORK / "core/assets/animalicacore/config/carrytypes/animalica-cradles.json"
    cradle_types = {entry["code"]: entry for entry in json.loads(style_path.read_text(encoding="utf-8"))}
    fox_scale = fox_eye_height / 1.7
    target = {}
    for style in STYLES:
        carry_type = cradle_types[style]
        ratio = 1 / fox_scale
        profiles = carry_type["sizeProfiles"]
        if ratio < max(profile["sizeRatio"] for profile in profiles):
            raise ValueError("Fox reference no longer uses the largest cradle profile")
        profile = max(profiles, key=lambda entry: entry["sizeRatio"])["values"]
        seat = []
        for axis in ("forward", "up", "right"):
            anchor = profile["anchor" + axis]
            tuned = profile["seat" + axis]
            seat.append(anchor - (anchor - tuned) * fox_scale)
        center = centers[REFERENCE][style]
        target[style] = [round(seat[0] + center[0] - 0.5, 5),
                         round(seat[1] + center[1] - SHARED_CRADLE_DROP, 5),
                         round(seat[2] + center[2] - 0.5, 5)]

    for styles in centers.values():
        for values in styles.values():
            if not all(math.isfinite(value) and abs(value) < 10 for value in values):
                raise ValueError(f"Implausible cradle body center: {values}")
    document = {
        "referenceModel": REFERENCE,
        "target": target,
        "centers": {code: {style: [round(value, 5) for value in position]
                           for style, position in styles.items()}
                    for code, styles in centers.items()},
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    generate()
