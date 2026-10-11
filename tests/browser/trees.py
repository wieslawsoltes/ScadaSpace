"""Real Uno Tree control tests: input, local state, data errors, automation peers and virtualization."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path('TestResults/browser-trees'); OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get('SCADASPACE_URL', 'http://127.0.0.1:8765/ScadaSpace/')
results, logs = [], []
def scalar(v):
    if v is None: return {'kind': 'Null'}
    if isinstance(v, bool): return {'kind': 'Boolean', 'boolean': v}
    if isinstance(v, (int, float)): return {'kind': 'Number', 'number': v}
    if isinstance(v, str): return {'kind': 'String', 'text': v}
    if isinstance(v, list): return {'kind': 'Array', 'items': [scalar(x) for x in v]}
    return {'kind': 'Object', 'fields': {k: scalar(x) for k, x in v.items()}}
def node(name, kind, x, y, width, height, props=None, bindings=None):
    return {'id': name, 'name': name, 'type': kind, 'x': x, 'y': y, 'width': width, 'height': height, 'props': {k: scalar(v) for k, v in (props or {}).items()}, 'bindings': bindings or {}}
def bind(source, kind='Property', write=False): return {'source': source, 'kind': kind, 'bidirectional': write}
items = [{'id': 'plant', 'label': 'Plant', 'expanded': True, 'items': [
    {'id': 'pump', 'label': 'Pump', 'data': {'pressure': 12}, 'items': [{'id': 'motor', 'label': 'Motor', 'data': 'M1'}]},
    {'id': 'tank', 'label': 'Tank', 'data': 'T1'}]}, {'id': 'spare', 'label': 'Spare'}]
def props(data=None, **extra):
    return {'items': data or [], 'selection': '', 'selectedPaths': [], 'selectionData': [], 'filter': '', 'showFilter': True, 'rowHeight': 28, 'interactable': True, **extra}
small = node('EquipmentTree', 'Tree', 20, 80, 440, 310, props(items, multiSelect=True), {
    'items': bind("if({[default]Mode} == 'bad', {root.custom.bad}, {root.custom.items})", 'Expression'),
    'selection': bind('root.custom.selection', write=True)})
small['events'] = {'onItemClicked': [{'kind': 'WriteTag', 'target': '[default]Clicked', 'value': scalar(7)}]}
root = node('TreeRoot', 'Container', 0, 0, 1000, 820)
root['custom'] = {'items': scalar(items), 'bad': scalar([1]), 'selection': scalar('')}
root['children'] = [node('Title', 'Label', 20, 12, 960, 38, {'text': 'TREE COMPONENT LAB · hierarchical selection and virtualization', 'fontSize': 22}), small,
    node('LocalTags', 'TagBrowseTree', 520, 80, 450, 310, props(root='[default]Equipment')),
    node('SelectionInput', 'TextField', 20, 420, 200, 36, {'text': ''}, {'text': bind('root.custom.selection', write=True)}),
    node('Mode', 'TextField', 250, 420, 180, 36, {'text': 'good'}, {'text': bind('[default]Mode', 'Tag', True)}),
    node('Readout', 'Label', 470, 420, 490, 44, {'text': ''}, {'text': bind('EquipmentTree.props.selectionData')}),
    node('LargeTree', 'Tree', 20, 500, 940, 280, props([{'id': 'tag-'+str(i), 'label': 'Measurement '+str(i).zfill(4), 'data': i} for i in range(1200)]))]
fixture = {'schemaVersion': 1, 'name': 'Tree Component Lab', 'views': [{'path': 'Trees', 'width': 1000, 'height': 820, 'root': root}],
    'pages': [{'path': '/', 'view': 'Trees'}], 'tags': [
        {'path': '[default]Mode', 'dataType': 'String', 'initialValue': scalar('good')},
        {'path': '[default]Clicked', 'dataType': 'Number', 'initialValue': scalar(0)},
        {'path': '[default]Equipment/Pump/Flow', 'initialValue': scalar(42)},
        {'path': '[default]Equipment/Tank/Level', 'initialValue': scalar(60)}], 'alarms': []}
fixture_file = OUT / 'fixture.json'; fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--disable-dev-shm-usage'])
    context = browser.new_context(viewport={'width': 1600, 'height': 1100}, accept_downloads=True)
    page = context.new_page()
    page.on('pageerror', lambda e: logs.append({'kind': 'pageerror', 'text': str(e)}))
    page.on('console', lambda e: logs.append({'kind': e.type, 'text': e.text}))
    def app(): return page.evaluate('window.scadaApp')
    def views(): return page.evaluate('window.scadaViews')
    def component(name): return views()[0]['components'][name]
    def tree(name='EquipmentTree'): return component(name)['tree']
    def wait(js): page.wait_for_function(js, timeout=30000)
    def click(box):
        assert box and box['width'] > 0 and box['height'] > 0, 'Missing actual rendered bounds'
        page.mouse.click(box['x']+box['width']/2, box['y']+box['height']/2)
    def command(name): click(app()['controls'][name]); page.wait_for_timeout(350)
    def row(path, name='EquipmentTree'): return next(r for r in tree(name)['rows'] if r['path'] == path)
    def select(path, name='EquipmentTree'):
        box = row(path, name)['bounds']; page.mouse.click(box['x']+min(box['width']-12, 125), box['y']+box['height']/2); page.wait_for_timeout(350)
    def toggle(path, name='EquipmentTree'): click(row(path, name)['toggle']); page.wait_for_timeout(450)
    def fill(box, value): click(box); page.keyboard.press('Control+a'); page.keyboard.insert_text(value); page.keyboard.press('Enter'); page.wait_for_timeout(400)
    def check(name, condition):
        results.append({'name': name, 'passed': bool(condition)})
        if not condition: raise AssertionError(name)
        print('PASS', name, flush=True)
    try:
        page.goto(BASE, wait_until='domcontentloaded', timeout=120000)
        page.wait_for_function('window.scadaApp?.ready && window.scadaApp.tick > 2', timeout=120000)
        with page.expect_file_chooser() as event: command('import')
        event.value.set_files(fixture_file)
        wait("window.scadaApp.project === 'Tree Component Lab'")
        command('fit'); command('preview')
        wait("window.scadaViews?.[0]?.components?.EquipmentTree?.tree?.rows.length > 0")
        check('tree fixture loads into the native Uno view host', tree()['count'] == 5 and tree()['visibleCount'] == 4 and tree()['enabled'])
        check('native row peers expose TreeItem and expansion state', all(r['role'] == 'TreeItem' for r in tree()['rows']) and row('0')['expansion'] == 'Expanded')
        rebuilds = tree()['rebuilds']; tick = app()['tick']
        wait(f'window.scadaApp.tick > {tick+2}')
        check('unchanged runtime ticks do not rebuild the tree', tree()['rebuilds'] == rebuilds)
        toggle('0/0')
        check('pointer chevron expands a nested branch', tree()['visibleCount'] == 5 and row('0/0')['expanded'])
        select('0/0/0')
        check('node selection carries an index path and contextual data', tree()['selection'] == '0/0/0' and tree()['selectionData']['items'][0]['fields']['value']['text'] == 'M1')
        check('item invocation executes its configured action', app()['tags']['[default]Clicked']['value']['number'] == 7)
        page.keyboard.press('ArrowLeft'); page.wait_for_timeout(400)
        check('Left moves keyboard selection to the parent', tree()['selection'] == '0/0')
        page.keyboard.press('ArrowLeft'); page.wait_for_timeout(400)
        check('Left collapses an expanded branch', tree()['visibleCount'] == 4)
        page.keyboard.press('ArrowRight'); page.wait_for_timeout(400)
        check('Right expands a keyboard-focused branch', tree()['visibleCount'] == 5)
        select('0/0'); page.keyboard.down('Control'); select('0/1'); page.keyboard.up('Control')
        check('Ctrl-click retains multiple selected nodes', set(tree()['selectedPaths']) == {'0/0','0/1'})
        fill(tree()['filter'], 'Motor')
        check('filter reveals matching ancestors', [r['label'] for r in tree()['rows']] == ['Plant','Pump','Motor'])
        fill(tree()['filter'], '')
        check('clearing a filter restores local expansion state', tree()['visibleCount'] == 5)
        fill(component('SelectionInput')['bounds'], '0/0/0')
        check('bidirectional external selection input selects the node', tree()['selection'] == '0/0/0' and tree()['selectedPaths'] == ['0/0/0'])
        fill(component('Mode')['bounds'], 'bad')
        check('malformed bound items disable interaction with a visible diagnostic', not tree()['enabled'] and 'objects' in tree()['error'])
        fill(component('Mode')['bounds'], 'good')
        check('valid data recovers the same control after a data error', tree()['enabled'] and tree()['error'] is None and tree()['count'] == 5)
        toggle('0/0', 'LocalTags'); toggle('0/0/0', 'LocalTags'); select('0/0/0/0', 'LocalTags')
        check('local tag browse selection returns the actual full tag path', tree('LocalTags')['selectionData']['items'][0]['fields']['value']['text'] == '[default]Equipment/Pump/Flow')
        check('large trees virtualize native row containers', tree('LargeTree')['count'] == 1200 and 0 < len(tree('LargeTree')['rows']) < 100)
        select('0', 'LargeTree'); page.keyboard.press('End')
        wait("window.scadaViews[0].components.LargeTree.tree.selection === '1199'")
        check('keyboard End reaches an initially unrealized row', tree('LargeTree')['selection'] == '1199' and any(r['path'] == '1199' for r in tree('LargeTree')['rows']))
        check('virtualized scrolling keeps realized row count bounded', len(tree('LargeTree')['rows']) < 100)
        page.screenshot(path=str(OUT/'tree-runtime.png'), full_page=True)
        with page.expect_download() as event: command('export')
        event.value.save_as(OUT/'runtime-export.json')
        exported = json.loads((OUT/'runtime-export.json').read_text())
        defaults = exported['views'][0]['root']
        check('runtime tree interactions do not change saved defaults', defaults['custom']['selection']['text'] == '' and defaults['custom']['items'] == scalar(items))
        check('tree views retain good binding quality', not app()['diagnostics'] and not app()['errors'] and not any(r['kind']=='pageerror' for r in logs))
        page.set_viewport_size({'width': 1024, 'height': 768}); command('fit')
        check('tree controls remain live at tablet width', tree()['enabled'] and tree('LargeTree')['count'] == 1200)
        page.screenshot(path=str(OUT/'tree-tablet.png'), full_page=True)
    except Exception as error:
        results.append({'name': 'tree browser run', 'passed': False, 'error': str(error)})
        page.screenshot(path=str(OUT/'failure.png'), full_page=True)
        raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results, indent=2))
        (OUT/'console.json').write_text(json.dumps(logs, indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'), indent=2))
        browser.close()
