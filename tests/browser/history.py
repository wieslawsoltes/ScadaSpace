"""Local history bindings through the actual Uno file picker, controls and tables."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path('TestResults/browser-history'); OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get('SCADASPACE_URL', 'http://127.0.0.1:8765/ScadaSpace/')
results, logs = [], []

def scalar(v):
    if v is None: return {'kind': 'Null'}
    if isinstance(v, bool): return {'kind': 'Boolean', 'boolean': v}
    if isinstance(v, (int, float)): return {'kind': 'Number', 'number': v}
    if isinstance(v, str): return {'kind': 'String', 'text': v}
    if isinstance(v, list): return {'kind': 'Array', 'items': [scalar(x) for x in v]}
    return {'kind': 'Object', 'fields': {k: scalar(v) for k, v in v.items()}}

def node(name, kind, x, y, w, h, props=None, bindings=None):
    return {'id': name, 'name': name, 'type': kind, 'x': x, 'y': y, 'width': w, 'height': h,
            'props': {k: scalar(v) for k, v in (props or {}).items()}, 'bindings': bindings or {}}

def binding(source, kind='Property', write=False):
    return {'source': source, 'kind': kind, 'bidirectional': write}

columns = [{'field': 'value', 'title': 'Value', 'type': 'Number'}, {'field': 'quality', 'title': 'Quality'},
           {'field': 'count', 'title': 'Count', 'type': 'Number'}, {'field': 'timestamp', 'title': 'Unix milliseconds', 'weight': 2}]
raw = json.dumps({'path': '[default]Value', 'start': '{root.custom.start}', 'end': 4102444800000, 'limit': '{root.custom.limit}'})
average = json.dumps({'path': '[default]Value', 'start': 0, 'end': 4102444800000, 'aggregate': 'SampleAverage', 'returnSize': 1})
root = node('Root', 'Container', 0, 0, 1000, 720)
root['custom'] = {k: scalar(v) for k, v in {'limit':100, 'start':0, 'badColumns':False, 'badBinding':False, 'columns':columns}.items()}
children = [
    node('Heading', 'Label', 20, 12, 960, 42, {'text': 'LOCAL HISTORY · cached queries and recovery', 'fontSize': 24}),
    node('Limit', 'NumericEntry', 20, 68, 160, 38, {'value':100, 'minimum':1, 'maximum':1000}, {'value':binding('root.custom.limit',write=True)}),
    node('LimitLabel', 'Label', 20, 110, 180, 24, {'text':'Maximum raw rows'}),
    node('Start', 'NumericEntry', 220, 68, 210, 38, {'value':0, 'minimum':0, 'maximum':5000000000000}, {'value':binding('root.custom.start',write=True)}),
    node('StartLabel', 'Label', 220, 110, 210, 24, {'text':'Start (Unix milliseconds)'}),
    node('BadColumns', 'CheckBox', 470, 68, 250, 38, {'value':False,'text':'Invalid column fixture'}, {'value':binding('root.custom.badColumns',write=True)}),
    node('BadBinding', 'CheckBox', 740, 68, 240, 38, {'value':False,'text':'Failed column binding'}, {'value':binding('root.custom.badBinding',write=True)}),
    node('RawA', 'Table', 20, 150, 460, 250, {'source':'data','data':[], 'columns':columns, 'editable':True},
         {'data':binding(raw,'TagHistory'), 'columns':binding("if({root.custom.badBinding}, 1/0, if({root.custom.badColumns}, 'invalid', {root.custom.columns}))",'Expression')}),
    node('RawB', 'Table', 520, 150, 460, 250, {'source':'data','data':[], 'columns':columns}, {'data':binding(raw,'TagHistory')}),
    node('AverageTable', 'Table', 20, 455, 660, 200, {'source':'data','data':[], 'columns':columns}, {'data':binding(average,'TagHistory')}),
    node('Average', 'LEDDisplay', 740, 470, 220, 72, {'value':0, 'unit':'sample mean'}, {'value':binding('AverageTable.props.data[0].value')}),
]
for name, value, x in [('Write20',20,740), ('Write30',30,860)]:
    button = node(name, 'Button', x, 560, 100, 38, {'text':'Write '+str(value)})
    button['events'] = {'onAction':[{'kind':'WriteTag','target':'[default]Value','value':scalar(value)}]}
    children.append(button)
root['children'] = children
fixture = {'schemaVersion':1,'name':'History Binding Lab','views':[{'path':'History','width':1000,'height':720,'root':root}],
           'pages':[{'path':'/','view':'History'}], 'tags':[{'path':'[default]Value','initialValue':scalar(10)}], 'alarms':[]}
fixture_file=OUT/'fixture.json'; fixture_file.write_text(json.dumps(fixture))

with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1600,'height':1000},accept_downloads=True)
    page=context.new_page()
    page.on('pageerror',lambda e: logs.append({'kind':'pageerror','text':str(e),'stack':e.stack,'lastCheck':results[-1]['name'] if results else 'startup'}))
    page.on('console',lambda e: logs.append({'kind':e.type,'text':e.text}))
    def app(): return page.evaluate('window.scadaApp')
    def view(): return page.evaluate('window.scadaViews[0]')
    def component(name): return view()['components'][name]
    def rows(name): return component(name)['rows']
    def wait(js): page.wait_for_function(js,timeout=30000)
    def click(box):
        assert box and box['width']>0 and box['height']>0,'Missing native control bounds'
        page.mouse.click(box['x']+box['width']/2,box['y']+box['height']/2)
    def command(name): click(app()['controls'][name]); page.wait_for_timeout(300)
    def press(name): click(component(name)['bounds']); page.wait_for_timeout(400)
    def fill(name,value):
        click(component(name)['bounds']); page.keyboard.press('Control+a'); page.keyboard.insert_text(str(value)); page.keyboard.press('Enter'); page.wait_for_timeout(450)
    def check(name,ok):
        results.append({'name':name,'passed':bool(ok)})
        if not ok: raise AssertionError(name)
        print('PASS',name,flush=True)
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000)
        page.wait_for_function('window.scadaApp?.ready && window.scadaApp.tick > 2',timeout=120000)
        with page.expect_file_chooser() as chosen: command('import')
        chosen.value.set_files(fixture_file)
        wait("window.scadaApp.project === 'History Binding Lab'")
        command('fit'); command('preview')
        wait("window.scadaViews?.[0]?.components?.RawA?.rows?.length === 1")
        check('history binding renders original typed measurements in both tables',rows('RawA')[0]['cells'][:3]==['10','Good','1'] and rows('RawB')==rows('RawA'))
        check('sample average produces one typed aggregate row',rows('AverageTable')[0]['cells'][:3]==['10','Good','1'])
        reads=view()['historyReads']; hits=view()['historyHits']; tick=app()['tick']
        wait('window.scadaApp.tick > '+str(tick+3))
        check('identical table queries reuse shared results across idle refreshes',reads==2 and view()['historyReads']==reads and view()['historyHits']>hits)
        press('Write20'); wait('window.scadaViews[0].components.RawA.rows.length === 2')
        press('Write30'); wait('window.scadaViews[0].components.RawB.rows.length === 3')
        check('native actions append history and invalidate both cached tables',[r['cells'][0] for r in rows('RawA')]==['10','20','30'] and rows('RawA')==rows('RawB'))
        check('live aggregate preserves sample count and mean',rows('AverageTable')[0]['cells'][:3]==['20','Good','3'] and component('Average')['value']['value']['number']==20)
        reads=view()['historyReads']; tick=app()['tick']; wait('window.scadaApp.tick > '+str(tick+3))
        check('stable history does not reallocate query documents each tick',view()['historyReads']==reads)
        fill('Limit',1); wait('window.scadaViews[0].components.RawA.tableError !== null')
        check('oversized raw queries clear stale rows instead of truncating',rows('RawA')==[] and rows('RawB')==[] and 'sample limit' in component('RawA')['tableError'])
        check('raw query failure does not erase the independent aggregate',rows('AverageTable')[0]['cells'][0]=='20' and component('AverageTable')['tableError'] is None)
        page.screenshot(path=str(OUT/'history-limit-error.png'),full_page=True)
        fill('Limit',100); wait('window.scadaViews[0].components.RawA.rows.length === 3 && window.scadaViews[0].components.RawA.tableError === null')
        check('valid row limits recover the existing tables',rows('RawA')==rows('RawB') and not view()['diagnostics'])
        fill('Start',4102444800001); wait('window.scadaViews[0].components.RawA.tableError !== null')
        check('native numeric entry retains large Unix millisecond values without overflow',component('Start')['value']['value']['number']==4102444800001 and component('Start')['numeric']['text']=='4102444800001' and component('Start')['numeric']['dependencyValue']==4102444800001 and not any(x['kind']=='pageerror' for x in logs))
        check('invalid time ranges leave no previous table measurements visible',rows('RawA')==[] and rows('RawB')==[])
        fill('Start',0); wait('window.scadaViews[0].components.RawA.rows.length === 3')
        fill('Start',2147483648); page.keyboard.press('ArrowUp'); page.wait_for_timeout(250)
        check('numeric keyboard steps preserve values beyond Int32',component('Start')['value']['value']['number']==2147483649)
        fill('Start','NaN')
        check('nonfinite numeric input leaves the previous finite value intact',component('Start')['value']['value']['number']==2147483649 and not any(x['kind']=='pageerror' for x in logs))
        click(component('Start')['bounds']); page.keyboard.press('Control+a'); page.keyboard.insert_text('12'); page.keyboard.press('Escape'); page.wait_for_timeout(250)
        check('numeric Escape restores canonical text without committing a draft',component('Start')['numeric']['text']=='2147483649' and component('Start')['numeric']['dependencyValue']==2147483649)
        fill('Start',0); wait('window.scadaViews[0].components.RawA.rows.length === 3')
        check('correcting time input restores the full result',component('RawA')['tableError'] is None and not view()['diagnostics'])
        press('BadColumns'); wait('window.scadaViews[0].components.RawA.tableError !== null')
        check('malformed column configuration clears only the affected table',rows('RawA')==[] and 'columns must be an array' in component('RawA')['tableError'] and len(rows('RawB'))==3)
        press('BadColumns'); wait('window.scadaViews[0].components.RawA.tableError === null && window.scadaViews[0].components.RawA.rows.length === 3')
        check('column configuration errors recover without replacing project data',len(rows('RawA'))==3)
        press('BadBinding'); wait('window.scadaViews[0].components.RawA.tableError !== null')
        check('failed column bindings cannot silently fall back to inferred columns',rows('RawA')==[] and 'Column binding quality: BadExpression' in component('RawA')['tableError'] and len(rows('RawB'))==3)
        press('BadBinding'); wait('window.scadaViews[0].components.RawA.tableError === null && window.scadaViews[0].components.RawA.rows.length === 3')
        check('recovering a column expression restores data and quality',not view()['diagnostics'] and len(rows('RawA'))==3)
        box=component('RawA')['bounds']; scale=box['width']/460
        x=box['x']+40*scale; y=box['y']+43*scale
        page.mouse.click(x,y); page.mouse.click(x,y); wait("window.scadaViews[0].components.RawA.rows[0].cells[0] === '30'")
        check('history tables preserve typed numeric sorting',[r['cells'][0] for r in rows('RawA')]==['30','20','10'])
        page.screenshot(path=str(OUT/'history-runtime.png'),full_page=True)
        with page.expect_download() as event: command('export')
        event.value.save_as(OUT/'runtime-export.json')
        exported=json.loads((OUT/'runtime-export.json').read_text())
        check('runtime history and control input do not mutate saved defaults',exported['tags'][0]['initialValue']['number']==10 and exported['views'][0]['root']['custom']['limit']['number']==100)
        check('history binding type and query options survive project export',next(n for n in exported['views'][0]['root']['children'] if n['name']=='RawA')['bindings']['data']['kind']=='TagHistory')
        page.set_viewport_size({'width':1024,'height':768}); command('fit')
        check('history binding tables remain live at tablet width',len(rows('RawA'))==3 and component('RawA')['tableError'] is None)
        page.screenshot(path=str(OUT/'history-tablet.png'),full_page=True)
        check('recovered bindings have no unhandled browser or managed errors',not view()['diagnostics'] and not app()['errors'] and not any(x['kind']=='pageerror' for x in logs))
    except Exception as error:
        results.append({'name':'history browser run','passed':False,'error':str(error)})
        page.screenshot(path=str(OUT/'failure.png'),full_page=True)
        raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2))
        (OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'),indent=2))
        browser.close()
