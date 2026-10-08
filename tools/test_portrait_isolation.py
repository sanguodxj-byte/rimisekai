import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location("portrait_isolation", Path(__file__).with_name("check_portrait_isolation.py"))
checks = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checks)


def shape(points):
    xs, ys = zip(*points)
    return {"x": min(xs), "y": min(ys), "w": max(xs) - min(xs), "h": max(ys) - min(ys), "polygon": points}


class PolygonIsolationTests(unittest.TestCase):
    def test_shared_edge_float_noise_is_not_area_overlap(self):
        left = shape([(0, 0), (400, 400), (0, 800)])
        right = shape([(0.0001, 0), (800, 0), (400, 400)])
        self.assertFalse(checks.shape_overlap(left, right))

    def test_one_pixel_overlap_is_rejected(self):
        left = shape([(0, 0), (400, 400), (0, 800)])
        right = shape([(-1, 0), (800, 0), (399, 400)])
        self.assertTrue(checks.shape_overlap(left, right))

    def test_identical_shapes_overlap(self):
        box = shape([(0, 0), (100, 0), (100, 100), (0, 100)])
        self.assertTrue(checks.shape_overlap(box, box))

    def test_containment_is_overlap(self):
        outer = shape([(0, 0), (100, 0), (100, 100), (0, 100)])
        inner = shape([(20, 20), (80, 20), (80, 80), (20, 80)])
        self.assertTrue(checks.shape_overlap(outer, inner))

    def test_edges_touch_without_overlap(self):
        left = shape([(0, 0), (100, 0), (100, 100), (0, 100)])
        right = shape([(100, 0), (200, 0), (200, 100), (100, 100)])
        self.assertFalse(checks.shape_overlap(left, right))

    def test_same_vertical_span_rectangles_overlap(self):
        cell = {"x": 798, "y": 500, "w": 172, "h": 172, "polygon": None}
        gate = {"x": 962, "y": 500, "w": 118, "h": 172, "polygon": None}
        self.assertTrue(checks.shape_overlap(cell, gate))

    def test_same_span_polygons_overlap(self):
        cell = shape([(798, 500), (970, 500), (970, 672), (798, 672)])
        gate = shape([(962, 500), (1080, 500), (1080, 672), (962, 672)])
        self.assertTrue(checks.shape_overlap(cell, gate))

    def test_cross_gate_into_its_gate_cell_is_known(self):
        cell = {"action": "Cell", "x": 454, "y": 760, "w": 172, "h": 172, "polygon": None}
        north = {"action": "CrossGate", "x": 454, "y": 706, "w": 172, "h": 118, "polygon": None}
        self.assertTrue(checks.known_overlap(north, cell))

    def test_cross_gate_past_half_a_cell_is_not_known(self):
        cell = {"action": "Cell", "x": 454, "y": 760, "w": 172, "h": 172, "polygon": None}
        deep = {"action": "CrossGate", "x": 454, "y": 706, "w": 172, "h": 200, "polygon": None}
        self.assertFalse(checks.known_overlap(deep, cell))

    def test_other_overlaps_are_not_known(self):
        a = {"action": "Cell", "x": 0, "y": 0, "w": 172, "h": 172, "polygon": None}
        b = {"action": "Tab", "x": 0, "y": 100, "w": 172, "h": 118, "polygon": None}
        self.assertFalse(checks.known_overlap(a, b))


if __name__ == "__main__":
    unittest.main()
