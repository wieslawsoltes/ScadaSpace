"""Perspective-style configuration workflows, exercised through native Uno controls only."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path('TestResults/browser-events'); OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get('SCADASPACE_URL', 'http://127.0.0.1:8765/ScadaSpace/')
results, logs = [], []
def scalar(value):
    if isinstance(value, bool): return {'kind':'Boolean', 'boolean':value}
    if isinstance(value, (int, float)): return {'kind':'Number', 'number':value}
    if isinstance(value, str): return {'kind':'String', 'text':value}
    return {'kind':'Null'}
def node(name, kind, x, y, w, h, **props):
    return {'id':name, 'name':name, 'type':kind, 'x':x, 'y':y, 'width':w, 'height':h, 'props':{k:scalar(v) for k,v in props.items()}}
def write(value): return {'kind':'WriteTag', 'target':'[default]Value', 'value':scalar(value)}
button = node('ActionButton', 'Button', 40, 130, 250, 60, text='Run configured actions')
label = node('ClickTarget', 'Label', 370, 130, 500, 60, text='Click / double-click / context menu', fontSize=19)
label['events'] = {'onClick':[write(17)], 'onDoubleClick':[write(51)], 'onContextMenu':[write(61)]}
output = node('Output', 'LEDDisplay', 40, 280, 250, 90, value=0)
output['bindings'] = {'value':{'kind':'Tag', 'source':'[default]Value'}}
root = node('Root', 'Container', 0, 0, 1000, 620)
root['children'] = [node('Heading','Label',40,25,900,55,text='EVENT CONFIGURATION · independent local actions',fontSize=24),button,label,output]
fixture = {'schemaVersion':1, 'name':'Event Configuration Lab', 'views':[{'path':'Main','width':1000,'height':620,'root':root}], 'pages':[{'path':'/','view':'Main'}], 'tags':[{'path':'[default]Value','initialValue':scalar(0)}], 'alarms':[]}
fixture_file = OUT/'fixture.json'; fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context = browser.new_context(viewport={'width':1600,'height':1000},accept_downloads=True)
    page = context.new_page()
    page.on('pageerror',lambda e: logs.append({'kind':'pageerror','text':str(e)}))
    page.on('console',lambda e: logs.append({'kind':e.type,'text':e.text}))
    def app(): return page.evaluate('window.scadaApp')
    def editor(): return app()['eventEditor']
    def wait(expression): page.wait_for_function(expression,timeout=30000)
    def click(box, button='left'):
        assert box and box['width']>0 and box['height']>0,'Native control is not arranged'
        page.mouse.click(box['x']+box['width']/2,box['y']+box['height']/2,button=button)
    def command(name):
        page.wait_for_function('key=>window.scadaApp.controls[key]?.width>0',arg=name,timeout=30000)
        click(app()['controls'][name]); page.wait_for_timeout(150)
    def component(name, button='left'):
        page.wait_for_function('name=>window.scadaApp.components[name]?.bounds?.width>0',arg=name,timeout=30000)
        click(app()['components'][name]['bounds'],button)
    def event_field(name):
        page.wait_for_function('key=>window.scadaApp.eventEditor?.fields[key]?.width>0',arg=name,timeout=30000)
        click(editor()['fields'][name]);page.wait_for_timeout(100)
    def fill_event(name, text):
        event_field(name);page.keyboard.press('Control+a');page.keyboard.press('Backspace');page.keyboard.insert_text(text)
    def open_events():
        command('menu-Component');command('configure-events');wait('window.scadaApp.eventEditor !== null')
    def explorer_field(name):
        page.wait_for_function('key=>window.scadaApp.configurationExplorer?.fields[key]?.width>0',arg=name,timeout=30000)
        click(app()['configurationExplorer']['fields'][name])
    def check(name, ok):
        results.append({'name':name,'passed':bool(ok)})
        if not ok: raise AssertionError(name)
        print('PASS',name,flush=True)
    def process_value(): return app()['tags']['[default]Value']['value']['number']
    def value_is(value): wait("window.scadaApp.tags['[default]Value'].value.number === "+str(value))
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000)
        page.wait_for_function('window.scadaApp?.ready && window.scadaApp.tick>2',timeout=120000)
        with page.expect_file_chooser() as chosen: command('import')
        chosen.value.set_files(fixture_file);wait("window.scadaApp.project === 'Event Configuration Lab'");command('fit')
        component('ActionButton');wait("window.scadaApp.selection[0] === 'ActionButton'");open_events()
        check('Component menu opens native three-pane event configuration',editor()['activeEvent']=='onActionPerformed' and editor()['count']==0 and editor()['fields']['event-onClick'] is not None)
        event_field('add');wait('window.scadaApp.eventEditor.count===1');fill_event('value','21')
        before=app()['generation'];command('configuration-dialog-apply');wait('window.scadaApp.generation>'+str(before))
        check('Apply commits a single undoable edit without closing or executing it',editor() is not None and editor()['error']=='' and not editor()['dirty'] and app()['canUndo'] and process_value()==0)
        before=app()['generation'];command('configuration-dialog-apply');tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+1))
        check('unchanged Apply does not create another document revision',app()['generation']==before)
        event_field('add');wait('window.scadaApp.eventEditor.count===2');fill_event('value','43');event_field('enabled');event_field('up');wait('window.scadaApp.eventEditor.selected===0')
        event_field('duplicate');wait('window.scadaApp.eventEditor.count===3');event_field('remove');wait('window.scadaApp.eventEditor.count===2')
        check('native action controls support reorder duplicate remove and disabled state',editor()['count']==2 and editor()['selected']==1)
        fill_event('value','invalid-number');before=app()['generation'];command('configuration-dialog-ok');wait("window.scadaApp.eventEditor?.error.length>0")
        check('invalid values keep the dialog open without committing a partial edit',app()['generation']==before and process_value()==0)
        page.screenshot(path=str(OUT/'event-validation.png'),full_page=True)
        fill_event('value','21');command('configuration-dialog-ok');wait('window.scadaApp.eventEditor===null')
        check('OK retains ordered staged actions as a document transaction',app()['generation']==before+1 and app()['dirty'] and process_value()==0)
        command('preview');wait('window.scadaApp.preview');component('ActionButton');value_is(21)
        check('onActionPerformed executes the enabled action and skips the disabled one',process_value()==21)
        component('ClickTarget');value_is(17)
        check('native onClick events execute configured declarative actions',process_value()==17)
        # A single-click followed immediately by dblclick is a three-tap gesture.
        # Uno's managed recognizer uses a 500 ms multi-tap interval; isolate test gestures.
        page.wait_for_timeout(600)
        box=app()['components']['ClickTarget']['bounds'];page.mouse.dblclick(box['x']+box['width']/2,box['y']+box['height']/2);value_is(51)
        check('native double-click raises onDoubleClick',process_value()==51)
        component('ClickTarget',button='right');value_is(61)
        check('runtime context-menu events execute without opening the designer menu',process_value()==61 and app()['eventEditor'] is None)
        command('preview');wait('!window.scadaApp.preview');component('ActionButton');wait("window.scadaApp.selection[0]==='ActionButton'");open_events()
        check('reopening the editor restores the complete action sequence',editor()['count']==2)
        page.screenshot(path=str(OUT/'event-configuration.png'),full_page=True)
        event_field('remove');wait('window.scadaApp.eventEditor.count===1');fill_event('value','99');before=app()['generation'];command('configuration-dialog-cancel');wait('window.scadaApp.eventEditor===null')
        check('Cancel discards removals and field edits without changing document history',app()['generation']==before)
        command('undo');wait('window.scadaApp.generation>'+str(before));before=app()['generation'];command('redo');wait('window.scadaApp.generation>'+str(before))
        component('ActionButton');wait("window.scadaApp.selection[0]==='ActionButton'");open_events();check('event configuration supports undo and redo',editor()['count']==2)
        command('configuration-dialog-cancel');wait('window.scadaApp.eventEditor===null')
        component('ClickTarget',button='right');command('context-events');wait('window.scadaApp.eventEditor!==null')
        check('canvas context menu selects its target before opening event configuration',app()['selection']==['ClickTarget'] and editor()['activeEvent']=='onClick')
        fill_event('value','19');command('configuration-dialog-ok');wait('window.scadaApp.eventEditor===null')
        command('menu-Component');command('configuration-explorer');wait('window.scadaApp.configurationExplorer!==null')
        total=app()['configurationExplorer']['count'];builds=app()['configurationExplorer']['builds']
        explorer_field('search');page.keyboard.insert_text('ActionButton');wait('window.scadaApp.configurationExplorer.count===3')
        check('Configuration Explorer indexes and filters configured events and actions',total>=7 and app()['configurationExplorer']['selected']['componentName']=='ActionButton')
        explorer_field('kind');page.keyboard.press('Home');page.keyboard.press('ArrowDown');page.keyboard.press('ArrowDown');page.keyboard.press('ArrowDown');page.keyboard.press('ArrowDown');page.keyboard.press('Enter')
        wait('window.scadaApp.configurationExplorer.count===2')
        check('configuration kind filter keeps action references and owned source details',app()['configurationExplorer']['selected']['kind']=='Action' and app()['configurationExplorer']['builds']==builds)
        page.screenshot(path=str(OUT/'configuration-explorer.png'),full_page=True)
        command('configuration-dialog-ok');wait('window.scadaApp.configurationExplorer===null')
        check('Go To Reference selects the referenced component in the active view',app()['selection']==['ActionButton'])
        command('menu-Component');command('configuration-explorer');wait('window.scadaApp.configurationExplorer!==null')
        check('reopening an unchanged inventory reuses its generation index',app()['configurationExplorer']['builds']==builds)
        command('configuration-dialog-cancel');wait('window.scadaApp.configurationExplorer===null')
        with page.expect_download() as event: command('export')
        event.value.save_as(OUT/'configured-project.json');exported=json.loads((OUT/'configured-project.json').read_text())
        configured=next(n for n in exported['views'][0]['root']['children'] if n['name']=='ActionButton')['events']['onActionPerformed']
        check('export preserves action order values and disabled configuration',len(configured)==2 and configured[0]['value']['number']==43 and not configured[0]['enabled'] and configured[1]['value']['number']==21 and configured[1]['enabled'])
        command('preview');wait('window.scadaApp.preview');component('ClickTarget');value_is(19)
        check('configured events still execute after editor and explorer navigation',process_value()==19)
        page.set_viewport_size({'width':1024,'height':768});command('fit');component('ActionButton');value_is(21)
        check('new event actions remain functional at tablet width',process_value()==21 and not app()['errors'])
        check('all configuration interactions finish without unhandled errors',not app()['errors'] and not app()['diagnostics'] and not any(e['kind']=='pageerror' for e in logs))
    except Exception as error:
        results.append({'name':'event configuration browser run','passed':False,'error':str(error)})
        page.screenshot(path=str(OUT/'failure.png'),full_page=True)
        raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('window.scadaApp||null'),indent=2));browser.close()
