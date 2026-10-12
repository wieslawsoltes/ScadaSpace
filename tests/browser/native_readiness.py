"""Passive native geometry readiness. Sends no managed command and never retries input."""
from __future__ import annotations
import math
import time
from typing import Any


class CommandReadiness:
    """Require equal, usable geometry across independently published native refreshes."""
    def __init__(self, samples: int = 3) -> None:
        if not isinstance(samples, int) or samples < 2:
            raise ValueError('At least two independent observations are required.')
        self.samples = samples
        self._tick = -1
        self._geometry: tuple | None = None
        self._count = 0

    def accept(self, sample: dict[str, Any]) -> bool:
        tick = sample.get('tick')
        if not isinstance(tick, int) or isinstance(tick, bool) or tick <= self._tick:
            return False
        self._tick = tick
        box, viewport = sample.get('bounds'), sample.get('viewport')
        if not isinstance(box, dict) or not isinstance(viewport, dict):
            self._geometry = None; self._count = 0
            return False
        values = tuple(box.get(key) for key in ('x', 'y', 'width', 'height')) + tuple(viewport.get(key) for key in ('width', 'height'))
        if any(not isinstance(value, (int, float)) or isinstance(value, bool) or not math.isfinite(value) for value in values):
            self._geometry = None; self._count = 0
            return False
        x, y, width, height, viewport_width, viewport_height = values
        if min(width, height, viewport_width, viewport_height) <= 0 or not (0 <= x + width / 2 < viewport_width and 0 <= y + height / 2 < viewport_height):
            self._geometry = None; self._count = 0
            return False
        geometry = values + (sample.get('project'), sample.get('viewId'))
        self._count = self._count + 1 if geometry == self._geometry else 1
        self._geometry = geometry
        return self._count >= self.samples


def wait_command_bounds(page: Any, key: str, timeout: int = 30000) -> dict[str, float]:
    """Return the latest stable bounds, without clicking or changing application state."""
    deadline = time.monotonic() + timeout / 1000
    tracker = CommandReadiness()
    sample = None
    while time.monotonic() < deadline:
        sample = page.evaluate('''key => ({
            tick: window.scadaApp?.tick,
            project: window.scadaApp?.project,
            viewId: window.scadaViews?.[0]?.id ?? null,
            viewport: window.scadaApp?.viewport,
            bounds: window.scadaApp?.controls[key]
        })''', key)
        if tracker.accept(sample):
            return sample['bounds']
        remaining = max(1, int((deadline - time.monotonic()) * 1000))
        page.wait_for_function('tick => Number.isInteger(window.scadaApp?.tick) && window.scadaApp.tick > tick',
                               arg=sample.get('tick', -1) if isinstance(sample.get('tick'), int) else -1,
                               timeout=remaining)
    raise TimeoutError(f'Native command geometry did not settle: {key}; last observation: {sample}')
