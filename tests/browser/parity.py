"""Structured binding/table/repeater integration through the real Uno browser UI."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright
from native_readiness import wait_command_bounds

OUT = Path("TestResults/browser-parity")
OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get("SCADASPACE_URL", "http://127.0.0.1:8765/ScadaSpace/")
results, logs = [], []

def scalar(value):
    if value is None: return {"kind": "Null"}
    if isinstance(value, bool): return {"kind": "Boolean", "boolean": value}
    if isinstance(value, (int, float)): return {"kind": "Number", "number": value}
    if isinstance(value, str): return {"kind": "String", "text": value}
    if isinstance(value, list): return {"kind": "Array", "items": [scalar(x) for x in value]}
    return {"kind": "Object", "fields": {k: scalar(v) for k, v in value.items()}}

def node(name, kind, x, y, w, h, props=None, bindings=None, children=None, custom=None):
    return {"id": name, "name": name, "type": kind, "x": x, "y": y, "width": w, "height": h,
            "props": {k: scalar(v) for k, v in (props or {}).items()}, "custom": {k: scalar(v) for k, v in (custom or {}).items()},
            "bindings": bindings or {}, "children": children or []}

def binding(source, write=False, kind="Property"):
    return {"kind": kind, "source": source, "bidirectional": write}

rows = [{"id": "a", "name": "Pump A", "value": 100}, {"id": "b", "name": "Pump B", "value": 2}, {"id": "c", "name": "Pump C", "value": 10}]
fixture = {"schemaVersion": 1, "name": "Structured Binding Lab", "views": [
    {"path": "BindingLab", "width": 1000, "height": 720, "root": node("LabRoot", "Container", 0, 0, 1000, 720, custom={"model": {"setpoint": 12, "count": 2, "rows": rows}}, children=[
        node("Heading", "Label", 24, 12, 900, 36, {"text": "STRUCTURED BINDING LAB · isolated runtime", "fontSize": 23}),
        node("Input", "NumericEntry", 24, 68, 160, 40, {"value": 12, "minimum": 0, "maximum": 100}, {"value": binding("root.custom.model.setpoint", True)}),
        node("Readout", "LEDDisplay", 220, 62, 160, 60, {"value": 12}, {"value": binding("root.custom.model.setpoint")}),
        node("Count", "NumericEntry", 450, 68, 130, 40, {"value": 2, "minimum": 0, "maximum": 4}, {"value": binding("root.custom.model.count", True)}),
        node("SelectedRow", "LEDDisplay", 650, 62, 140, 60, {"value": -1, "unit": "row"}, {"value": binding("DataTable.props.selection.row")}),
        node("EditedValue", "LEDDisplay", 820, 62, 140, 60, {"value": 2}, {"value": binding("root.custom.model.rows[1].value")}),
        node("DataTable", "Table", 24, 155, 580, 255, {"source": "data", "data": rows, "editable": True, "rowKey": "id", "selection": {"row": -1}, "columns": [{"field": "name", "title": "Equipment", "weight": 2, "editable": False}, {"field": "value", "title": "Value", "type": "Number"}]}, {"data": binding("root.custom.model.rows", True)}),
        node("Structure", "Label", 640, 155, 320, 160, {"text": ""}, {"text": binding(json.dumps({"setpoint": "{root.custom.model.setpoint}", "state": "'ready'"}), kind="ExpressionStructure")}),
        node("Repeater", "FlexRepeater", 24, 450, 930, 210, {"view": "Card", "count": 2, "params": {"setpoint": 12}, "gap": 12}, {"count": binding("root.custom.model.count"), "params.setpoint": binding("root.custom.model.setpoint")})
    ])},
    {"path": "Card", "width": 300, "height": 200, "parameters": {"setpoint": scalar(0), "index": scalar(0)}, "root": node("CardRoot", "Container", 0, 0, 300, 200, children=[
        node("CardTitle", "Label", 12, 8, 280, 35, {"text": ""}, {"text": binding("'Card ' + {view.params.index}", kind="Expression")}),
        node("CardValue", "LEDDisplay", 12, 52, 270, 62, {"value": 0}, {"value": binding("view.params.setpoint")}),
        node("LocalValue", "NumericEntry", 12, 136, 150, 40, {"value": 5, "minimum": 0, "maximum": 100})
    ])}
], "pages": [{"path": "/", "view": "BindingLab", "title": "Binding Lab"}], "tags": [], "alarms": []}
fixture_path = OUT / "fixture.json"
fixture_path.write_text(json.dumps(fixture))

with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=["--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--disable-dev-shm-usage"])
    context = browser.new_context(viewport={"width": 1600, "height": 1000}, accept_downloads=True)
    page = context.new_page()
    page.on("console", lambda m: logs.append({"kind": m.type, "text": m.text}))
    page.on("pageerror", lambda e: logs.append({"kind": "pageerror", "text": str(e)}))
    def state(): return page.evaluate("window.scadaApp")
    def views(): return page.evaluate("window.scadaViews")
    def check(name, condition):
        results.append({"name": name, "passed": bool(condition)})
        if not condition: raise AssertionError(name)
        print("PASS", name, flush=True)
    def wait(expression): page.wait_for_function(expression, timeout=30000)
    def click_box(box):
        assert box and box["width"] > 0, "Missing rendered bounds"
        page.mouse.click(box["x"] + box["width"] / 2, box["y"] + box["height"] / 2)
    def command(name):
        click_box(wait_command_bounds(page, name)); page.wait_for_timeout(400)
    def enter(name, value, instance=0):
        click_box(views()[instance]["components"][name]["bounds"])
        page.keyboard.press("Control+a"); page.keyboard.insert_text(str(value)); page.keyboard.press("Enter"); page.wait_for_timeout(500)
    def number(name, instance=0): return views()[instance]["components"][name]["value"]["value"]["number"]
    try:
        page.goto(BASE, wait_until="domcontentloaded", timeout=120000)
        page.wait_for_function("window.scadaApp?.ready && window.scadaApp.tick > 2 && window.scadaViews?.[0]?.components.Header?.symbol?.renders > 0", timeout=120000)
        with page.expect_file_chooser() as chooser:
            command("import")
        chooser.value.set_files(fixture_path)
        wait("window.scadaApp.project === 'Structured Binding Lab'")
        command("fit"); command("preview")
        wait("window.scadaViews?.length === 3 && window.scadaViews[0].view === 'BindingLab'")
        check("structured project imports through the file picker", state()["project"] == fixture["name"])
        check("two actual embedded instances initialize", len(views()) == 3)
        check("structured bindings initialize without diagnostics", all(not v["diagnostics"] for v in views()))
        enter("Input", 77)
        wait("window.scadaViews[0].components.Readout.value.value.number === 77")
        check("property-bound numeric input propagates", number("Readout") == 77)
        check("expression structure is rendered from evaluated leaves", json.loads(views()[0]["components"]["Structure"]["text"]["value"]["text"])["setpoint"] == 77 if views()[0]["components"]["Structure"]["text"]["value"]["kind"] == "String" else views()[0]["components"]["Structure"]["text"]["value"]["fields"]["setpoint"]["number"] == 77)
        check("embedded parameters react without reload", number("CardValue", 1) == 77 and number("CardValue", 2) == 77)
        ids = [v["id"] for v in views()[1:]]
        enter("LocalValue", 9, 1); enter("Input", 33)
        check("repeated views preserve local state across parent updates", number("LocalValue", 1) == 9 and number("LocalValue", 2) == 5 and [v["id"] for v in views()[1:]] == ids)
        enter("Count", 3); wait("window.scadaViews.length === 4")
        check("repeater creates new instances reactively", number("LocalValue", 1) == 9 and number("LocalValue", 3) == 5)
        enter("Count", 2); wait("window.scadaViews.length === 3")
        check("repeater removes surplus instances", [v["id"] for v in views()[1:]] == ids)
        table = views()[0]["components"]["DataTable"]["bounds"]; scale = table["width"] / 580
        x = table["x"] + table["width"] * .82
        page.mouse.click(x, table["y"] + 43 * scale)
        wait("window.scadaViews[0].components.DataTable.rows[0].cells[0] === 'Pump B'")
        check("real table header sorts numbers rather than strings", [r["cells"][1] for r in views()[0]["components"]["DataTable"]["rows"]] == ["2", "10", "100"])
        page.mouse.dblclick(x, table["y"] + 70 * scale, delay=80); page.wait_for_timeout(500)
        page.keyboard.press("Control+a"); page.keyboard.insert_text("invalid"); page.keyboard.press("Enter"); page.wait_for_timeout(350)
        check("cell editor rejects invalid numeric input", number("EditedValue") == 2)
        page.keyboard.press("Control+a"); page.keyboard.insert_text("31"); page.keyboard.press("Enter")
        wait("window.scadaViews[0].components.EditedValue.value.value.number === 31")
        check("sorted table edit updates the correct source row", number("EditedValue") == 31)
        page.mouse.click(table["x"] + 90 * scale, table["y"] + 15 * scale)
        page.keyboard.insert_text("Pump A")
        wait("window.scadaViews[0].components.DataTable.rows.length === 1")
        page.mouse.click(x, table["y"] + 70 * scale); page.wait_for_timeout(400)
        check("filtered row selection retains source identity", number("SelectedRow") == 0)
        page.screenshot(path=str(OUT / "structured-runtime.png"), full_page=True)
        with page.expect_download() as event: command("export")
        event.value.save_as(OUT / "export.json")
        exported = json.loads((OUT / "export.json").read_text())
        original = exported["views"][0]["root"]["custom"]["model"]["fields"]
        check("runtime edits do not mutate exported project defaults", original["setpoint"]["number"] == 12 and original["rows"]["items"][1]["fields"]["value"]["number"] == 2)
        page.set_viewport_size({"width": 1024, "height": 768}); command("fit")
        check("structured runtime remains live at tablet width", len(views()) == 3 and all(not v["diagnostics"] for v in views()))
        page.screenshot(path=str(OUT / "structured-tablet.png"), full_page=True)
        check("no managed or JavaScript exceptions", not state()["errors"] and not any(x["kind"] == "pageerror" for x in logs))
    except Exception as error:
        results.append({"name": "parity browser run", "passed": False, "error": str(error)})
        page.screenshot(path=str(OUT / "failure.png"), full_page=True)
        raise
    finally:
        (OUT / "results.json").write_text(json.dumps(results, indent=2))
        (OUT / "console.json").write_text(json.dumps(logs, indent=2))
        (OUT / "last-state.json").write_text(json.dumps(page.evaluate("({app:window.scadaApp,views:window.scadaViews})"), indent=2))
        browser.close()
