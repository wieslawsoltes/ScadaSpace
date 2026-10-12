"""Native container-wrapping/source-navigation and rollback/layout-cache regressions."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path('TestResults/browser-hierarchy'); OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get('SCADASPACE_URL', 'http://127.0.0.1:8765/ScadaSpace/')
results, logs = [], []
def scalar(v):
    if isinstance(v, bool): return {'kind':'Boolean','boolean':v}
    if isinstance(v, (int,float)): return {'kind':'Number','number':v}
    if isinstance(v, str): return {'kind':'String','text':v}
    return {'kind':'Null'}
def node(name, kind, x, y, w, h, **props):
    return {'id':name,'name':name,'type':kind,'x':x,'y':y,'width':w,'height':h,'props':{k:scalar(v) for k,v in props.items()}}
root = node('Root', 'Container', 0, 0, 1000, 650, padding=10)
root['custom'] = {'level':scalar(12)}
a = node('InputA','NumericEntry',60,100,180,40,value=12,minimum=0,maximum=100)
a['bindings'] = {'value':{'kind':'Property','source':'parent.custom.level','bidirectional':True}}
b = node('ActionB','Button',280,100,180,40,text='Write test value')
b['events'] = {'onActionPerformed':[{'kind':'WriteTag','target':'[default]Test','value':scalar(77)}]}
readout = node('Readout','Label',60,210,450,50,text='',fontSize=24)
readout['bindings'] = {'text':{'kind':'Expression','source':"concat('Level: ', tostring({InputA.props.value}))"}}
embedded = node('Embedded','EmbeddedView',600,80,300,130,view='DetailA')
embedded['bindings'] = {'view':{'kind':'Expression','source':"'DetailB'"}}
repeater = node('Repeater','FlexRepeater',600,280,300,130,view='DetailA',count=1,gap=0)
missing = node('Missing','EmbeddedView',600,470,300,100,view='MissingView')
root['children'] = [node('Heading','Label',40,20,900,45,text='DESIGNER WORKFLOWS · hierarchy and source views',fontSize=24),a,b,readout,embedded,repeater,missing]
views = [{'path':'Main','width':1000,'height':650,'root':root}]
for name in ['DetailA','DetailB']:
    child_root = node(name+'Root','Container',0,0,300,130)
    child_root['children'] = [node(name+'Label','Label',10,10,270,80,text='Source '+name,fontSize=24)]
    views.append({'path':name,'width':300,'height':130,'root':child_root})
fixture = {'schemaVersion':1,'name':'Hierarchy Workflow Lab','views':views,'pages':[{'path':'/','view':'Main'}], 'tags':[{'path':'[default]Test','initialValue':scalar(0)}], 'alarms':[]}
fixture_file = OUT/'fixture.json'; fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1600,'height':1000},accept_downloads=True)
    page=context.new_page()
    page.on('pageerror',lambda e:logs.append({'kind':'pageerror','text':str(e)}))
    page.on('console',lambda e:logs.append({'kind':e.type,'text':e.text}))
    def app(): return page.evaluate('window.scadaApp')
    def wait(expr): page.wait_for_function(expr,timeout=30000)
    def fresh():
        tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+1))
    def click(box,button='left'):
        assert box and box['width']>0 and box['height']>0,'Native geometry unavailable'
        page.mouse.click(box['x']+box['width']/2,box['y']+box['height']/2,button=button)
    def command(key):
        page.wait_for_function('key=>window.scadaApp.controls[key]?.width>0',arg=key,timeout=30000)
        click(app()['controls'][key]);fresh()
    def component(name,button='left'):
        page.wait_for_function('name=>window.scadaApp.components[name]?.bounds?.width>0',arg=name,timeout=30000)
        click(app()['components'][name]['bounds'],button);fresh()
    def submenu(key):
        page.wait_for_function('key=>window.scadaApp.controls[key]?.width>0',arg=key,timeout=30000)
        box=app()['controls'][key];page.mouse.move(box['x']+box['width']/2,box['y']+box['height']/2)
    def check(name,ok):
        results.append({'name':name,'passed':bool(ok)})
        if not ok: raise AssertionError(name)
        print('PASS',name,flush=True)
    def close_enough(a,b): return all(abs(a[k]-b[k])<1 for k in ('x','y','width','height'))
    def inspector(name,text):
        page.wait_for_function('name=>window.scadaApp.inspector[name]?.width>0',arg=name,timeout=30000)
        click(app()['inspector'][name]);page.keyboard.press('Control+a');page.keyboard.insert_text(text);page.keyboard.press('Enter');fresh()
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000)
        page.wait_for_function('window.scadaApp?.ready && window.scadaApp.tick>3',timeout=120000)
        with page.expect_file_chooser() as chosen: command('import')
        chosen.value.set_files(fixture_file);wait("window.scadaApp.project==='Hierarchy Workflow Lab'");command('fit')
        component('InputA');page.keyboard.down('Control');component('ActionB');page.keyboard.up('Control')
        check('native additive selection selects two sibling components',set(app()['selection'])=={'InputA','ActionB'})
        before=app();component('InputA','right');submenu('context-wrap')
        page.wait_for_function("window.scadaApp.controls['context-wrap-coordinate']?.width>0")
        page.screenshot(path=str(OUT/'wrap-context-menu.png'),full_page=True)
        command('context-wrap-coordinate');wait("window.scadaApp.selection[0]==='CoordinateContainer'")
        after=app();group=after['components']['CoordinateContainer']['id']
        check('context Wrap In Container creates one undoable group',after['generation']==before['generation']+1 and after['componentCount']==before['componentCount']+1 and after['canUndo'])
        check('wrapped children retain stable component IDs and parent membership',after['components']['InputA']['id']=='InputA' and after['components']['InputA']['parentId']==group and after['components']['ActionB']['parentId']==group)
        check('wrapping preserves native child and neighboring geometry',all(close_enough(before['components'][n]['bounds'],after['components'][n]['bounds']) for n in ['InputA','ActionB','Readout','Embedded']))
        page.screenshot(path=str(OUT/'wrapped-components.png'),full_page=True)
        command('preview');wait('window.scadaApp.preview');component('InputA');page.keyboard.press('Control+a');page.keyboard.insert_text('42');page.keyboard.press('Enter')
        wait("window.scadaApp.components.Readout.text.value.text==='Level: 42'")
        check('wrapped bidirectional input still writes through its original parent binding',app()['components']['InputA']['value']['value']['number']==42)
        component('ActionB');wait("window.scadaApp.tags['[default]Test'].value.number===77")
        check('wrapping retains operational component event actions',app()['tags']['[default]Test']['value']['number']==77)
        with page.expect_download() as download: command('export')
        download.value.save_as(OUT/'wrapped-project.json');exported=json.loads((OUT/'wrapped-project.json').read_text())
        saved_root=exported['views'][0]['root'];saved_group=next(n for n in saved_root['children'] if n['id']==group)
        check('export preserves stacking order and original project defaults', [n['id'] for n in saved_root['children']][:3]==['Heading',group,'Readout'] and saved_root['custom']['level']['number']==12)
        check('export rewrites parent bindings rather than dropping binding semantics',saved_group['children'][0]['bindings']['value']['source']=='root.custom.level' and saved_group['children'][0]['bindings']['value']['bidirectional'])
        command('preview');command('undo');wait("window.scadaApp.components.InputA.parentId==='Root'")
        check('Undo restores the original hierarchy in a clean document',not app()['dirty'] and app()['componentCount']==before['componentCount'])
        command('redo');wait("window.scadaApp.components.InputA.parentId!== 'Root'")
        check('Redo restores the same wrapper identity',app()['components']['InputA']['parentId']==group)
        component('InputA');generation=app()['generation'];identity=page.evaluate('window.scadaViews[0].id')
        inspector('width','-1')
        wait("window.scadaViews[0].id!=="+json.dumps(identity))
        check('rejected inspector edits rebind the restored model without committing',app()['generation']==generation and app()['components']['InputA']['width']==180 and not app()['hierarchy']['restoreErrors'])
        inspector('width','200');wait('window.scadaApp.components.InputA.width===200')
        check('valid edits work immediately after a rollback',app()['generation']==generation+1)
        command('undo');wait('window.scadaApp.components.InputA.width===180')
        component('InputA');fresh();hierarchy=app()['hierarchy'].copy()
        for _ in range(3): component('ActionB');component('InputA')
        check('selection and redraw reuse the hierarchy and flattened layout caches',app()['hierarchy']['builds']==hierarchy['builds'] and app()['hierarchy']['layoutBuilds']==hierarchy['layoutBuilds'])
        generation=app()['generation'];command('close-view-DetailB');component('Embedded','right');command('context-modify-source')
        wait("window.scadaApp.view==='DetailB'")
        check('Modify Source View uses the evaluated embedded path not its default',app()['view']=='DetailB' and 'DetailBLabel' in app()['components'])
        check('source navigation reopens closed resource tabs without editing the project',app()['generation']==generation and 'DetailB' in app()['workspace']['open'])
        command('view-Main');component('Repeater','right');command('context-modify-source');wait("window.scadaApp.view==='DetailA'")
        check('repeater context navigation opens its reusable source view', 'DetailALabel' in app()['components'])
        command('view-Main');component('Missing','right');command('context-modify-source')
        check('missing source views are diagnosed without leaving the current design',app()['view']=='Main' and 'MissingView' in (app()['hierarchy']['sourceError'] or '') and app()['generation']==generation)
        component('InputA');page.keyboard.down('Control');component('Embedded');page.keyboard.up('Control')
        generation=app()['generation'];command('menu-Component');submenu('wrap-container');command('wrap-coordinate')
        check('mixed-parent wrapping fails without modifying either subtree',app()['generation']==generation and app()['components']['InputA']['parentId']==group and app()['components']['Embedded']['parentId']=='Root')
        component('InputA');page.set_viewport_size({'width':1024,'height':768})
        wait('window.scadaApp.viewport.width===1024');fresh();command('fit')
        wait('Math.abs(window.scadaApp.components.InputA.bounds.width - 180*window.scadaApp.zoom)<1')
        component('ActionB')
        check('scaled tablet pointer input still selects the intended wrapped component',app()['selection']==['ActionB'])
        page.screenshot(path=str(OUT/'hierarchy-tablet.png'),full_page=True)
        check('hierarchy workflows finish without unhandled managed or browser errors',not app()['errors'] and not app()['diagnostics'] and not any(x['kind']=='pageerror' for x in logs))
    except Exception as error:
        results.append({'name':'hierarchy browser run','passed':False,'error':str(error)})
        page.screenshot(path=str(OUT/'failure.png'),full_page=True);raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'),indent=2));browser.close()
