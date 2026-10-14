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
        identity = self.identity(sample)
        if identity is None:
            self._geometry = None; self._count = 0
            return False
        geometry = values + identity
        self._count = self._count + 1 if geometry == self._geometry else 1
        self._geometry = geometry
        return self._count >= self.samples


    def identity(self, sample: dict[str, Any]) -> tuple | None:
        return (sample.get('project'), sample.get('viewId'))


class ComponentReadiness(CommandReadiness):
    """Unrotated pixel-layout designer selection, not arbitrary transformed hit testing."""
    def identity(self, sample: dict[str, Any]) -> tuple | None:
        layout, box, zoom = sample.get('layout'), sample['bounds'], sample.get('zoom')
        if not isinstance(layout, dict) or not isinstance(layout.get('bounds'), dict):
            return None
        values = (zoom, layout.get('width'), layout.get('height'))
        if any(not isinstance(v, (int, float)) or isinstance(v, bool) or not math.isfinite(v) or v <= 0 for v in values):
            return None
        if sample.get('editing') is not True or sample.get('preview') is not False:
            return None
        if not layout.get('id') or not sample.get('viewId') or sample.get('view') != sample.get('observedView'):
            return None
        # Positive bounds alone can still belong to the pre-Fit layout. Require both
        # independently observed native rectangles to agree with the declared size.
        for key in ('width', 'height'):
            if abs(box[key] - layout[key] * zoom) >= 1:
                return None
        for key in ('x', 'y', 'width', 'height'):
            actual = layout['bounds'].get(key)
            if not isinstance(actual, (int, float)) or isinstance(actual, bool) or not math.isfinite(actual) or abs(actual - box[key]) >= 1:
                return None
        return super().identity(sample) + (layout['id'], sample.get('generation'), zoom, layout['width'], layout['height'])



class InspectorFieldReadiness(CommandReadiness):
    """Require a current visible qualified field and a stable inspector projection."""
    def identity(self, sample: dict[str, Any]) -> tuple | None:
        if sample.get('visible') is not True or sample.get('preview') is not False:
            return None
        selection = sample.get('selection')
        if not isinstance(selection, list) or len(selection) != 1 or not isinstance(selection[0], str) or not selection[0]:
            return None
        keys = ('path', 'viewId', 'syntax', 'mode')
        if any(not isinstance(sample.get(key), str) or not sample[key] for key in keys):
            return None
        if not isinstance(sample.get('query'), str):
            return None
        revisions = tuple(sample.get(key) for key in ('generation', 'inspection', 'filterRevision'))
        if any(not isinstance(value, int) or isinstance(value, bool) or value < 0 for value in revisions):
            return None
        flags = tuple(sample.get(key) for key in ('caseSensitive', 'boundOnly'))
        if any(not isinstance(value, bool) for value in flags):
            return None
        return super().identity(sample) + (sample['path'], tuple(selection), sample['query'], sample['syntax'], sample['mode']) + revisions + flags


def wait_component_sample(page: Any, name: str, timeout: int = 30000) -> dict[str, Any]:
    """Wait for current scaled native geometry; the caller sends exactly one pointer input."""
    deadline = time.monotonic() + timeout / 1000
    tracker = ComponentReadiness()
    sample = None
    while time.monotonic() < deadline:
        sample = page.evaluate("""name => {
            const app = window.scadaApp, view = window.scadaViews?.[0];
            return {
                tick: app?.tick, project: app?.project, generation: app?.generation,
                view: app?.view, observedView: view?.view, viewId: view?.id,
                viewport: app?.viewport, zoom: app?.zoom, preview: app?.preview,
                editing: view?.editing, layout: app?.components?.[name],
                bounds: view?.components?.[name]?.bounds
            };
        }""", name)
        if tracker.accept(sample):
            return sample
        remaining = max(1, int((deadline - time.monotonic()) * 1000))
        page.wait_for_function('tick => Number.isInteger(window.scadaApp?.tick) && window.scadaApp.tick > tick',
                               arg=sample.get('tick', -1) if isinstance(sample.get('tick'), int) else -1,
                               timeout=remaining)
    raise TimeoutError(f'Native component geometry did not settle: {name}; last observation: {sample}')


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


def wait_inspector_field_sample(page: Any, path: str, timeout: int = 30000) -> dict[str, Any]:
    """Observe settled field layout without expanding categories or retrying input."""
    deadline = time.monotonic() + timeout / 1000
    tracker = InspectorFieldReadiness()
    sample = None
    while time.monotonic() < deadline:
        sample = page.evaluate("""path => {
            const app = window.scadaApp, filter = app?.inspectorFilter;
            return {
                tick: app?.tick, project: app?.project, viewId: window.scadaViews?.[0]?.id,
                viewport: app?.viewport, bounds: app?.inspectorQualified?.[path],
                path, selection: app?.selection, generation: app?.generation, preview: app?.preview,
                inspection: filter?.builds, filterRevision: filter?.applies,
                query: filter?.query, syntax: filter?.syntax, mode: filter?.mode,
                caseSensitive: filter?.caseSensitive, boundOnly: filter?.boundOnly,
                visible: filter?.visible?.includes(path) === true
            };
        }""", path)
        if tracker.accept(sample):
            return sample
        remaining = max(1, int((deadline - time.monotonic()) * 1000))
        page.wait_for_function('tick => Number.isInteger(window.scadaApp?.tick) && window.scadaApp.tick > tick',
                               arg=sample.get('tick', -1) if isinstance(sample.get('tick'), int) else -1,
                               timeout=remaining)
    raise TimeoutError(f'Native inspector field did not settle: {path}; last observation: {sample}')
