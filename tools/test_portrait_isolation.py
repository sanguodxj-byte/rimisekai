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


if __name__ == "__main__":
    unittest.main()
