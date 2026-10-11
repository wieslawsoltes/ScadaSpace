"""Page editor, hash navigation and real docked view interaction on the published Uno app."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path("TestResults/browser-pages")
OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get("SCADASPACE_URL", "http://127.0.0.1:8765/ScadaSpace/")
results, logs = [], []

def scalar(v):
    if v is None: return {"kind": "Null"}
    if isinstance(v, bool): return {"kind": "Boolean", "boolean": v}
    if isinstance(v, (float, int)): return {"kind": "Number", "number": v}
    if isinstance(v, str): return {"kind": "String", "text": v}
    if isinstance(v, list): return {"kind": "Array", "items": [scalar(x) for x in v]}
    return {"kind": "Object", "fields": {k: scalar(x) for k, x in v.items()}}

def node(name, kind, x, y, w, h, props=None, source=None, actions=None):
    n = {"id": name, "name": name, "type": kind, "x": x, "y": y, "width": w, "height": h, "props": {k: scalar(v) for k, v in (props or {}).items()}}
    if source: n["bindings"] = {"text": {"kind": "Expression", "source": source}}
    if actions: n["events"] = {"onAction": actions}
    return n

def view(path, w, h, children, parameters=None):
    root = node(path + "Root", "Container", 0, 0, w, h); root["children"] = children
    return {"path": path, "width": w, "height": h, "root": root, "parameters": {k: scalar(v) for k, v in (parameters or {}).items()}}

def button(name, text, x, target, kind="Navigate", parameters=None):
    return node(name, "Button", x, 12, 180, 40, {"text": text}, actions=[{"kind": kind, "target": target, "parameters": {k: scalar(v) for k, v in (parameters or {}).items()}}])

fixture = {"schemaVersion": 1, "name": "Page Navigation Lab", "views": [
    view("Home", 1000, 620, [node("HomeTitle", "Label", 30, 80, 800, 70, {"text": "PAGE NAVIGATION LAB", "fontSize": 30}), node("NestedHost", "EmbeddedView", 30, 220, 900, 240, {"view": "NestedA"})]),
    view("NestedA", 900, 240, [node("NestedBHost", "EmbeddedView", 0, 0, 900, 220, {"view": "NestedB"})]),
    view("NestedB", 900, 220, [node("NestedCHost", "EmbeddedView", 0, 0, 900, 180, {"view": "NestedC"})]),
    view("NestedC", 900, 180, [node("NestedLeaf", "Label", 0, 0, 900, 100, {"text": "", "fontSize": 24}, "'Page ' + {page.props.path}")]),
    view("Equipment", 1000, 620, [
        node("EquipmentTitle", "Label", 30, 90, 860, 65, {"text": "", "fontSize": 30}, "'Equipment ' + {view.params.equipment}"),
        node("QueryLabel", "Label", 30, 175, 800, 45, {"text": "", "fontSize": 20}, "'Mode: ' + {page.props.urlParams.mode}"),
        button("OpenDetails", "Open modal details", 30, "details", "OpenDock", {"station": "selected"})
    ], {"equipment": "default"}),
    view("Navigation", 1000, 64, [button("GoHome", "Home", 12, "/"), button("GoA", "Equipment A", 210, "/equipment/A?mode=auto"), button("GoB", "Equipment B", 408, "/equipment/B?mode=manual")]),
    view("Sidebar", 200, 600, [node("SidebarLabel", "Label", 12, 35, 175, 70, {"text": "Shared local state", "fontSize": 19}), node("LocalCounter", "NumericEntry", 12, 125, 175, 40, {"value": 5, "minimum": 0, "maximum": 100})]),
    view("Details", 280, 600, [node("DetailsTitle", "Label", 12, 80, 250, 60, {"text": "", "fontSize": 20}, "'Station: ' + {view.params.station}"), button("CloseDetails", "Close details", 12, "details", "CloseDock")], {"station": "default"})
], "pages": [
    {"path": "/", "view": "Home", "title": "Home"},
    {"path": "/equipment/:equipment", "view": "Equipment", "title": "Equipment", "docks": [{"id": "details", "view": "Details", "edge": "East", "display": "OnDemand", "content": "Cover", "size": 280, "modal": True}]},
    {"path": "/equipment/all", "view": "Home", "title": "All equipment"}
], "sharedDocks": [
    {"id": "nav", "view": "Navigation", "edge": "North", "display": "Visible", "content": "Push", "size": 64, "minimumSize": 32, "resizable": False},
    {"id": "side", "view": "Sidebar", "edge": "West", "display": "Auto", "content": "Auto", "size": 200, "minimumSize": 80, "maximumSize": 400, "autoBreakpoint": 1200}
], "tags": [], "alarms": []}
fixture_file = OUT / "fixture.json"; fixture_file.write_text(json.dumps(fixture))

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--disable-dev-shm-usage"])
    context = browser.new_context(viewport={"width": 1600, "height": 1000}, accept_downloads=True)
    page = context.new_page()
    def watch(target):
        target.on("pageerror", lambda e: logs.append({"type": "pageerror", "text": str(e)}))
        target.on("console", lambda e: logs.append({"type": e.type, "text": e.text}))
    watch(page)
    def app(): return page.evaluate("window.scadaApp")
    def wait(js): page.wait_for_function(js, timeout=30000)
    def click(bounds):
        assert bounds and bounds["width"] > 0 and bounds["height"] > 0, "Missing UI bounds"
        page.mouse.click(bounds["x"] + bounds["width"] / 2, bounds["y"] + bounds["height"] / 2)
    def command(name): click(app()["controls"][name]); page.wait_for_timeout(350)
    def dock(name): return next(d for d in app()["page"]["docks"] if d["id"] == name)
    def component(name, owner=None): return dock(owner)["components"][name] if owner else app()["components"][name]
    def press(name, owner=None): click(component(name, owner)["bounds"]); page.wait_for_timeout(450)
    def fill(bounds, text):
        click(bounds); page.keyboard.press("Control+a"); page.keyboard.insert_text(str(text))
    def check(name, ok):
        results.append({"name": name, "passed": bool(ok)})
        if not ok: raise AssertionError(name)
        print("PASS", name, flush=True)
    def route(url):
        fill(app()["controls"]["session-url"], url); page.keyboard.press("Enter"); page.wait_for_timeout(500)
    try:
        page.goto(BASE, wait_until="domcontentloaded", timeout=120000)
        page.wait_for_function("window.scadaApp?.ready && window.scadaApp.tick > 2", timeout=120000)
        with page.expect_file_chooser() as event: command("import")
        event.value.set_files(fixture_file)
        wait("window.scadaApp.project === 'Page Navigation Lab'")
        command("page-configuration"); wait("window.scadaApp.pageEditor?.add")
        click(app()["pageEditor"]["add"]); page.wait_for_timeout(500)
        fill(app()["pageEditor"]["path"], "/maintenance")
        fill(app()["pageEditor"]["title"], "Maintenance")
        page.keyboard.press("Enter"); wait("window.scadaApp.pageEditor === null")
        check("page configuration commits as an undoable designer transaction", app()["canUndo"] and app()["dirty"])
        with page.expect_download() as event: command("export")
        event.value.save_as(OUT / "configured-project.json")
        configured = json.loads((OUT / "configured-project.json").read_text())
        check("page editor preserves docks and adds the configured route", any(v["path"] == "/maintenance" for v in configured["pages"]) and len(configured["sharedDocks"]) == 2)
        command("undo"); check("page configuration supports undo", not app()["dirty"])
        command("redo"); command("save")
        with page.expect_popup() as event: command("launch-session")
        designer = page; page = event.value; watch(page)
        page.wait_for_function("window.scadaApp?.page && window.scadaApp.tick > 2", timeout=120000)
        check("isolated session starts at the root page", app()["sessionMode"] and app()["page"]["url"] == "/")
        check("shared push docks reserve primary view space", app()["page"]["primary"]["x"] == 200 and app()["page"]["primary"]["y"] == 64)
        observed = page.evaluate("window.scadaViews")
        check("nested page context reaches every child on its first refresh", [v["view"] for v in observed] == ["Home", "NestedA", "NestedB", "NestedC"] and observed[-1]["components"]["NestedLeaf"]["text"]["value"]["text"] == "Page /" and all(not v["diagnostics"] for v in observed))
        before_evaluations = {v["id"]: v["evaluations"] for v in observed}
        before_indexes = {v["id"]: v["bindingIndexBuilds"] for v in observed}
        tick = app()["tick"]
        wait("window.scadaApp.tick >= " + str(tick + 4))
        after_evaluations = page.evaluate("window.scadaViews")
        increments = [v["evaluations"] - before_evaluations[v["id"]] for v in after_evaluations]
        check("nested views evaluate once per steady-state refresh regardless of depth", increments[0] > 0 and all(n == increments[0] for n in increments))
        check("idle browser ticks reuse binding indexes", all(v["bindingIndexBuilds"] == before_indexes[v["id"]] for v in after_evaluations))
        initial = dock("side")["instance"]
        fill(component("LocalCounter", "side")["bounds"], 9); page.keyboard.press("Enter"); page.wait_for_timeout(350)
        press("GoA", "nav"); wait("window.scadaApp.page.url === '/equipment/A?mode=auto'")
        check("URL captures reach the primary view", component("EquipmentTitle")["text"]["value"]["text"] == "Equipment A")
        check("query parameters are exposed separately", component("QueryLabel")["text"]["value"]["text"] == "Mode: auto")
        check("shared dock view identity and local input survive navigation", dock("side")["instance"] == initial and component("LocalCounter", "side")["value"]["value"]["number"] == 9)
        primary = app()["page"]["primary"]
        press("OpenDetails"); wait("window.scadaApp.page.docks.some(d=>d.id==='details' && d.open)")
        check("modal cover docks do not resize the primary view", app()["page"]["primary"] == primary and dock("details")["modal"] and not dock("details")["pushes"])
        check("dock action parameters reach their view", component("DetailsTitle", "details")["text"]["value"]["text"] == "Station: selected")
        page.screenshot(path=str(OUT / "modal-dock.png"), full_page=True)
        press("CloseDetails", "details"); wait("window.scadaApp.page.docks.some(d=>d.id==='details' && !d.open)")
        check("declarative close action dismisses the dock", not dock("details")["open"])
        press("GoB", "nav"); wait("window.scadaApp.page.url === '/equipment/B?mode=manual'")
        check("same-view navigation refreshes route and query inputs", component("EquipmentTitle")["text"]["value"]["text"] == "Equipment B" and component("QueryLabel")["text"]["value"]["text"] == "Mode: manual")
        page.go_back(); wait("window.scadaApp.page.url === '/equipment/A?mode=auto'")
        check("browser Back restores the previous parameter snapshot", component("EquipmentTitle")["text"]["value"]["text"] == "Equipment A")
        page.go_forward(); wait("window.scadaApp.page.url === '/equipment/B?mode=manual'")
        command("session-back"); wait("window.scadaApp.page.url === '/equipment/A?mode=auto'")
        check("application Back stays synchronized with browser history", page.url.endswith("#/equipment/A?mode=auto"))
        grip = dock("side")["splitter"]; x = grip["x"] + grip["width"] / 2; y = grip["y"] + grip["height"] / 2
        page.mouse.move(x,y); page.mouse.down(); page.mouse.move(x+70,y,steps=7); page.mouse.up()
        wait("window.scadaApp.page.docks.find(d=>d.id==='side').size > 250")
        check("native splitter changes dock and primary geometry", app()["page"]["primary"]["x"] > 250)
        page.set_viewport_size({"width": 1000, "height": 800}); wait("!window.scadaApp.page.docks.find(d=>d.id==='side').open")
        check("auto dock closes below its breakpoint", app()["page"]["primary"]["x"] == 0)
        click(dock("side")["handle"]); wait("window.scadaApp.page.docks.find(d=>d.id==='side').open")
        check("narrow-screen dock handle opens a cover view", not dock("side")["pushes"] and app()["page"]["primary"]["x"] == 0)
        page.screenshot(path=str(OUT / "responsive-dock.png"), full_page=True)
        page.set_viewport_size({"width": 1600, "height": 1000}); wait("window.scadaApp.page.docks.find(d=>d.id==='side').pushes")
        route("/maintenance"); check("designer-configured page runs in the isolated session", app()["page"]["url"] == "/maintenance" and app()["view"] == "Home")
        route("/missing"); check("invalid navigation leaves the active page unchanged", app()["page"]["url"] == "/maintenance" and "Unknown page" in app()["status"])
        route("/equipment/Łódź?mode=auto+%2B+manual")
        expected_url = app()["page"]["url"]
        check("Unicode route captures and escaped query delimiters reach native labels", component("EquipmentTitle")["text"]["value"]["text"] == "Equipment Łódź" and component("QueryLabel")["text"]["value"]["text"] == "Mode: auto + manual")
        page.reload(wait_until="domcontentloaded")
        page.wait_for_function("window.scadaApp?.page && window.scadaApp.tick > 2", timeout=120000)
        check("canonical Unicode page URL survives a full browser reload", app()["page"]["url"] == expected_url and component("EquipmentTitle")["text"]["value"]["text"] == "Equipment Łódź")
        route("/equipment/" + "界" * 500)
        check("oversized encoded URLs fail without changing the active page", app()["page"]["url"] == expected_url and "length limit" in app()["status"])
        route("/equipment/A?mode=%FF")
        check("malformed UTF-8 is rejected through the native navigation UI", app()["page"]["url"] == expected_url and "UTF-8" in app()["status"])
        route("/equipment/all"); check("static route wins over the dynamic pattern", app()["view"] == "Home")
        with page.expect_download() as event: command("export")
        event.value.save_as(OUT / "runtime-export.json")
        exported = json.loads((OUT / "runtime-export.json").read_text())
        check("runtime resizing does not mutate saved dock definitions", exported["sharedDocks"][1]["size"] == 200)
        other = context.new_page(); watch(other); other.goto(BASE + "?session=1#/equipment/Pump%20Z?mode=remote", wait_until="domcontentloaded", timeout=120000)
        other.wait_for_function("window.scadaApp?.page && window.scadaApp.tick > 2", timeout=120000)
        deep = other.evaluate("window.scadaApp")
        check("direct deep links initialize route captures and query values", deep["components"]["EquipmentTitle"]["text"]["value"]["text"] == "Equipment Pump Z" and deep["page"]["query"]["mode"]["text"] == "remote")
        check("separate browser sessions isolate dock runtime state", next(d for d in deep["page"]["docks"] if d["id"] == "side")["components"]["LocalCounter"]["value"]["value"]["number"] == 5)
        check("no page binding or session observer failures", not app()["diagnostics"] and not app()["page"]["diagnostics"] and not deep["diagnostics"] and not deep["errors"] and not app()["errors"] and not any(e["type"] == "pageerror" for e in logs))
        other.screenshot(path=str(OUT / "deep-link.png"), full_page=True); other.close(); designer.close()
    except Exception as error:
        results.append({"name": "page integration run", "passed": False, "error": str(error)})
        page.screenshot(path=str(OUT / "failure.png"), full_page=True)
        raise
    finally:
        (OUT / "results.json").write_text(json.dumps(results, indent=2))
        (OUT / "console.json").write_text(json.dumps(logs, indent=2))
        (OUT / "last-state.json").write_text(json.dumps(page.evaluate("window.scadaApp || null"), indent=2))
        browser.close()
