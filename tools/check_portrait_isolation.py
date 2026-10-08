#!/usr/bin/env python3
"""Check the portrait capture's actual regions, controls, and polygon hit areas."""
import json
import math
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
DUMP = Path(os.environ.get("PORTRAIT_DUMP", ROOT / "_ui_mobile/portrait_widgets.jsonl"))
WIDTH, HEIGHT, TOUCH = 1080, 2340, 118
EPS = 0.01


def right(r):
    return r["x"] + r["w"]


def bottom(r):
    return r["y"] + r["h"]


def contains(a, b):
    return (a["x"] <= b["x"] + EPS and a["y"] <= b["y"] + EPS
            and right(a) + EPS >= right(b) and bottom(a) + EPS >= bottom(b))


def box_overlap(a, b):
    return min(right(a), right(b)) - max(a["x"], b["x"]) > EPS and min(bottom(a), bottom(b)) - max(a["y"], b["y"]) > EPS


def polygon(r):
    if r.get("polygon"):
        return r["polygon"]
    return [(r["x"], r["y"]), (right(r), r["y"]), (right(r), bottom(r)), (r["x"], bottom(r))]


def cross(a, b, p):
    return (b[0] - a[0]) * (p[1] - a[1]) - (b[1] - a[1]) * (p[0] - a[0])


def edge_distance(a, b, p):
    length = math.hypot(b[0] - a[0], b[1] - a[1])
    return cross(a, b, p) / length if length else 0.0


def strict_inside(p, poly):
    inside = False
    for a, b in zip(poly, poly[1:] + poly[:1]):
        if abs(edge_distance(a, b, p)) <= EPS and min(a[0], b[0]) - EPS <= p[0] <= max(a[0], b[0]) + EPS and min(a[1], b[1]) - EPS <= p[1] <= max(a[1], b[1]) + EPS:
            return False
        if (a[1] > p[1]) != (b[1] > p[1]) and p[0] < a[0] + (b[0] - a[0]) * (p[1] - a[1]) / (b[1] - a[1]):
            inside = not inside
    return inside


def overlap_box(a, b):
    x0, y0 = max(a["x"], b["x"]), max(a["y"], b["y"])
    return {"x": x0, "y": y0, "w": min(right(a), right(b)) - x0, "h": min(bottom(a), bottom(b)) - y0}


def shape_overlap(a, b):
    if not box_overlap(a, b):
        return False
    # Two plain rectangles: a positive-area box intersection is the overlap. Edge-crossing tests alone miss
    # rectangles that share the same span on one axis (all their edges are collinear or disjoint).
    if not a.get("polygon") and not b.get("polygon"):
        return True
    aa, bb = polygon(a), polygon(b)
    for p, q in zip(aa, aa[1:] + aa[:1]):
        for r, s in zip(bb, bb[1:] + bb[:1]):
            pq = edge_distance(p, q, r), edge_distance(p, q, s)
            rs = edge_distance(r, s, p), edge_distance(r, s, q)
            if min(pq) < -EPS and max(pq) > EPS and min(rs) < -EPS and max(rs) > EPS:
                return True
    if any(strict_inside(p, bb) for p in aa) or any(strict_inside(p, aa) for p in bb):
        return True
    for poly in (aa, bb):
        center = (sum(p[0] for p in poly) / len(poly), sum(p[1] for p in poly) / len(poly))
        if strict_inside(center, aa) and strict_inside(center, bb):
            return True
    # Polygons whose edges only touch or run collinear: sample the shared box interior.
    box = overlap_box(a, b)
    for i in range(1, 8):
        for j in range(1, 8):
            p = (box["x"] + box["w"] * i / 8, box["y"] + box["h"] * j / 8)
            if strict_inside(p, aa) and strict_inside(p, bb):
                return True
    return False


def known_overlap(a, b):
    """The border arrow's hit reaches into its own gate room cell to make 118px (owner-approved, 2026-10-08).
    Allowed only against one Cell it shares a full edge with, and only as deep as the 118 minimum needs."""
    if {a["action"], b["action"]} != {"CrossGate", "Cell"}:
        return False
    gate, cell = (a, b) if a["action"] == "CrossGate" else (b, a)
    box = overlap_box(gate, cell)
    along_x = abs(gate["w"] - cell["w"]) <= EPS and abs(gate["x"] - cell["x"]) <= EPS
    along_y = abs(gate["h"] - cell["h"]) <= EPS and abs(gate["y"] - cell["y"]) <= EPS
    depth = box["h"] if along_x else box["w"]
    return (along_x or along_y) and contains(cell, box) and depth < min(cell["w"], cell["h"]) / 2


def label(row):
    return f"{row['action']}#{row['index']} {row['label']}"


def main():
    if not DUMP.exists():
        print(f"Run PortraitCapture.tscn with --pdump={DUMP} first.")
        return 2
    pages = {}
    for line in DUMP.read_text(encoding="utf-8").splitlines():
        row = json.loads(line)
        pages.setdefault(row["page"], []).append(row)
    errors = []
    for page, rows in sorted(pages.items()):
        before = len(errors)
        regions = [r for r in rows if r["kind"] == "region"]
        widgets = [r for r in rows if r["kind"] == "widget"]
        for row in rows:
            if row["x"] < -EPS or row["y"] < -EPS or right(row) > WIDTH + EPS or bottom(row) > HEIGHT + EPS or min(row["w"], row["h"]) <= 0:
                errors.append(f"{page}: out of canvas {label(row)}")
        for i, a in enumerate(regions):
            for b in regions[i + 1:]:
                if box_overlap(a, b):
                    errors.append(f"{page}: region overlap {a['action']} / {b['action']}")
        for widget in widgets:
            if widget["enabled"] and min(widget["w"], widget["h"]) < TOUCH - EPS:
                errors.append(f"{page}: touch target below {TOUCH}px {label(widget)}")
            if regions:
                owner = next((r for r in regions if r["action"] == widget["owner"]), None)
                if owner is None or not contains(owner, widget):
                    errors.append(f"{page}: widget outside owning region {label(widget)}")
            if widget.get("polygon") and any(not (widget["x"] - EPS <= p[0] <= right(widget) + EPS and widget["y"] - EPS <= p[1] <= bottom(widget) + EPS) for p in widget["polygon"]):
                errors.append(f"{page}: polygon exceeds reported bounds {label(widget)}")
        for i, a in enumerate(widgets):
            for b in widgets[i + 1:]:
                # A sector label and its sector polygon are the same action, not competing controls.
                if a["action"] == b["action"] and a["index"] == b["index"]:
                    continue
                # Perspective cards/columns share a single enemy stage and use reverse draw-order hits.
                # Their containment, target size and frontmost eligibility are checked separately.
                if a["owner"] == b["owner"] == "enemies":
                    continue
                if shape_overlap(a, b):
                    if known_overlap(a, b):
                        if sum(1 for c in widgets if c["action"] == "Cell" and shape_overlap(c, a if a["action"] == "CrossGate" else b)) == 1:
                            continue
                    errors.append(f"{page}: competing targets overlap {label(a)} / {label(b)}")
        print(f"{page}: {len(regions)} regions, {len(widgets)} targets, {len(errors) - before} errors")
    for error in errors:
        print("ERROR " + error)
    if errors:
        return 1
    print("PASS: actual regions isolated; targets contained; polygon hits checked; enabled targets >=118px.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
