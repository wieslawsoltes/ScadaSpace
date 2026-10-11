"""Exercise the actual Uno WASM app with pointer/keyboard input; telemetry is observation-only."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path("TestResults/browser")
OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get("SCADASPACE_URL", "http://127.0.0.1:8765/ScadaSpace/")
results: list[dict] = []
logs: list[dict] = []

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--disable-dev-shm-usage"])
    context = browser.new_context(viewport={"width": 1600, "height": 1000}, device_scale_factor=1, accept_downloads=True)
    page = context.new_page()
    page.on("console", lambda m: logs.append({"kind": m.type, "text": m.text}))
    page.on("pageerror", lambda e: logs.append({"kind": "pageerror", "text": str(e)}))
    page.on("requestfailed", lambda r: logs.append({"kind": "requestfailed", "url": r.url, "failure": r.failure}))
    def check(name: str, condition: bool) -> None:
        results.append({"name": name, "passed": bool(condition)})
        if not condition:
            raise AssertionError(name)
        print("PASS", name, flush=True)
    def state() -> dict:
        return page.evaluate("window.scadaApp")
    def click_command(name: str) -> None:
        page.wait_for_function("name => window.scadaApp?.controls?.[name]?.width > 0", arg=name)
        box = state()["controls"][name]
        page.mouse.click(box["x"] + box["width"] / 2, box["y"] + box["height"] / 2)
        page.wait_for_timeout(350)
    def click_component(name: str) -> None:
        box = state()["components"][name]["bounds"]
        assert box, name + " has no actual rendered bounds"
        page.mouse.click(box["x"] + box["width"] / 2, box["y"] + box["height"] / 2)
        page.wait_for_timeout(350)
    try:
        page.goto(BASE, wait_until="domcontentloaded", timeout=120000)
        page.wait_for_function("window.scadaApp?.ready === true", timeout=120000)
        page.wait_for_function("window.scadaApp?.tick > 3", timeout=30000)
        click_command("fit")
        check("Uno view host initialized", state()["view"] == "Overview")
        font = page.evaluate("window.scadaFont")
        (OUT / "font.json").write_text(json.dumps(font, indent=2))
        check("bundled Open Sans is decoded by Skia", font["loaded"] and "opensans" in font["family"].replace(" ", "").lower() and not font["error"])
        check("drawing text uses proportional glyph advances", 0 < font["narrowAdvance"] < font["wideAdvance"] * .6)
        check("eight real in-memory tags", len(state()["tags"]) == 8)
        check("historian collects live samples", state()["historyCount"] >= 3)
        check("no binding diagnostics in demonstration", not state()["diagnostics"])
        check("no unhandled managed errors", not state()["errors"])
        page.screenshot(path=str(OUT / "designer.png"), full_page=True)
        click_component("Tank01")
        check("pointer selects the actual tank", state()["selection"] == ["Tank01"])
        before = state()["components"]["Tank01"]["x"]
        box = state()["components"]["Tank01"]["bounds"]
        zoom = state()["zoom"]
        page.mouse.move(box["x"] + box["width"] / 2, box["y"] + box["height"] / 2)
        page.mouse.down()
        page.mouse.move(box["x"] + box["width"] / 2 + 16 * zoom, box["y"] + box["height"] / 2 + 8 * zoom, steps=8)
        page.mouse.up()
        page.wait_for_timeout(500)
        check("snapped designer drag creates an undoable edit", state()["components"]["Tank01"]["x"] == before + 16 and state()["canUndo"])
        click_command("undo")
        check("undo restores exact component geometry", state()["components"]["Tank01"]["x"] == before)
        click_command("preview")
        check("preview switches to interactive controls", state()["preview"])
        click_component("StopPump")
        page.wait_for_function("window.scadaApp.tags['[default]Plant/Pump/Enabled'].value.boolean === false")
        check("button action writes the simulated tag", state()["tags"]["[default]Plant/Pump/Enabled"]["value"]["boolean"] is False)
        check("tag write activates the pump-stopped alarm", any(a["alarmId"] == "pump-stopped" and a["active"] for a in state()["alarms"]))
        click_component("LevelSetpoint")
        page.keyboard.press("Control+a")
        page.keyboard.insert_text("85")
        page.keyboard.press("Enter")
        page.wait_for_timeout(500)
        check("numeric entry uses bidirectional binding", state()["tags"]["[default]Plant/Tank01/Setpoint"]["value"]["number"] == 85)
        page.screenshot(path=str(OUT / "runtime.png"), full_page=True)
        click_command("view-Trends")
        click_command("fit")
        check("view navigation loads live charts", state()["view"] == "Trends")
        page.screenshot(path=str(OUT / "trends.png"), full_page=True)
        click_command("view-Alarms")
        click_command("fit")
        check("alarm view renders without binding errors", state()["view"] == "Alarms" and not state()["diagnostics"])
        page.screenshot(path=str(OUT / "alarms.png"), full_page=True)
        click_command("view-Overview")
        click_command("fit")
        with page.expect_download() as event:
            click_command("export")
        download = event.value
        destination = OUT / "roundtrip-project.json"
        download.save_as(destination)
        project = json.loads(destination.read_text())
        check("project export contains real views and tags", len(project["views"]) == 5 and len(project["tags"]) == 8)
        click_command("save")
        page.reload(wait_until="domcontentloaded")
        page.wait_for_function("window.scadaApp?.ready && window.scadaApp.tick > 2", timeout=120000)
        check("saved project reloads from browser storage", state()["project"] == project["name"])
        page.set_viewport_size({"width": 1024, "height": 768})
        page.wait_for_timeout(500)
        click_command("fit")
        page.screenshot(path=str(OUT / "tablet.png"), full_page=True)
        check("tablet viewport remains live", state()["viewport"]["width"] == 1024 and not state()["errors"])
        graphics = page.evaluate("window.scadaGraphics || null")
        (OUT / "graphics.json").write_text(json.dumps(graphics, indent=2))
        check("Skia acquired an actual browser graphics context", bool(graphics and any(x in graphics["contexts"] for x in ["webgl", "webgl2", "experimental-webgl"])))
        check("no JavaScript page exceptions", not any(x["kind"] == "pageerror" for x in logs))
    except Exception as error:
        results.append({"name": "browser run", "passed": False, "error": str(error)})
        page.screenshot(path=str(OUT / "failure.png"), full_page=True)
        raise
    finally:
        (OUT / "results.json").write_text(json.dumps(results, indent=2))
        (OUT / "console.json").write_text(json.dumps(logs, indent=2))
        (OUT / "last-state.json").write_text(json.dumps(page.evaluate("window.scadaApp || null"), indent=2))
        browser.close()
