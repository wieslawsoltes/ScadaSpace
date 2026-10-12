"""Native symbol-state authoring, theme application and retained effect playback.
Every write is a pointer/keyboard/file-picker action; observation objects expose no commands.
"""
from __future__ import annotations
import json
import os
import time
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path('TestResults/browser-symbol-states'); OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get('SCADASPACE_URL', 'http://127.0.0.1:8765/ScadaSpace/')
results, logs = [], []
def scalar(value):
    if isinstance(value, bool): return {'kind':'Boolean','boolean':value}
    if isinstance(value, (int,float)): return {'kind':'Number','number':value}
    if isinstance(value, str): return {'kind':'String','text':value}
    return {'kind':'Null'}
def node(name, kind, x, y, width, height, **props):
    return {'id':name,'name':name,'type':kind,'x':x,'y':y,'width':width,'height':height,
            'props':{k:scalar(v) for k,v in props.items()}}
root = node('Root','Container',0,0,1000,620)
root['custom'] = {'state':scalar('default')}
root['children'] = [node('Heading','Label',25,15,930,42,text='PROJECT SYMBOL STATES · native light / dark authoring',fontSize=24),
                    node('StateInput','TextField',30,85,220,38,text='default')]
root['children'][1]['bindings'] = {'text':{'kind':'Property','source':'root.custom.state','bidirectional':True}}
for index, kind in enumerate(['Pump','Motor','Valve','Sensor','Vessel']):
    symbol = node(kind,kind,25+index*195,165,170,340,value=45,appearance='simple',state='default',
                  orientation='vertical' if kind=='Vessel' else 'right',animationSpeed=50,label=kind)
    if kind=='Motor': symbol['bindings'] = {'state':{'kind':'Property','source':'root.custom.state'}}
    root['children'].append(symbol)
fixture = {'schemaVersion':1,'name':'Symbol State Lab','views':[{'path':'Main','width':1000,'height':620,'root':root}],
           'pages':[{'path':'/','view':'Main'}],'tags':[],'alarms':[]}
fixture_file=OUT/'fixture.json'; fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1700,'height':1100},accept_downloads=True)
    page=context.new_page()
    page.on('pageerror',lambda error:logs.append({'kind':'pageerror','text':str(error)}))
    page.on('console',lambda event:logs.append({'kind':event.type,'text':event.text}))
    def app(): return page.evaluate('window.scadaApp')
    def editor(): return page.evaluate('window.scadaSymbolEditor')
    def symbol(name): return page.evaluate('name=>window.scadaViews[0].components[name].symbol',name)
    def wait(expression): page.wait_for_function(expression,timeout=30000)
    def ticks(count=2):
        current=app()['tick']; wait('window.scadaApp.tick>'+str(current+count))
    def click(box):
        assert box and box['width']>0 and box['height']>0,'Native target must have arranged bounds'
        page.mouse.click(box['x']+box['width']/2,box['y']+box['height']/2)
    def command(key):
        page.wait_for_function('key=>window.scadaApp.controls[key]?.width>0',arg=key,timeout=30000)
        click(app()['controls'][key])
    def visible_field(key, library=False):
        expression='window.scadaApp.symbolLibrary' if library else 'window.scadaSymbolEditor'
        page.wait_for_function('key=>'+expression+'?.fields[key]?.width>0',arg=key,timeout=30000)
        for _ in range(5):
            snapshot=page.evaluate(expression); box=snapshot['fields'][key]; clip=snapshot['fields']['settings-scroll']
            center=box['y']+box['height']/2
            if box['x']<clip['x'] or clip['y']+3<center<clip['y']+clip['height']-3: return box
            page.mouse.move(clip['x']+clip['width']*.8,clip['y']+clip['height']/2)
            page.mouse.wheel(0,center-(clip['y']+clip['height']/2))
            page.wait_for_function('p=>Math.abs('+expression+'.fields[p.key].y-p.y)>1',arg={'key':key,'y':box['y']},timeout=10000)
        raise AssertionError('Could not scroll native field into its viewport: '+key)
    def field(key, text=None):
        click(visible_field(key))
        if text is not None:
            page.keyboard.press('Control+a');page.keyboard.press('Backspace')
            if text: page.keyboard.insert_text(text)
        ticks(1)
    def open_editor():
        command('menu-Project'); command('symbol-states')
        wait('window.scadaSymbolEditor?.fields.primary?.width>0')
    def choose_theme(theme):
        field('theme')
        page.wait_for_function('name=>window.scadaSymbolEditor.themeMenu?.some(row=>row.name===name && row.bounds?.width>0)',arg=theme,timeout=30000)
        click(next(row['bounds'] for row in editor()['themeMenu'] if row['name']==theme))
        wait('window.scadaSymbolEditor.theme==='+json.dumps(theme)+' && window.scadaSymbolEditor.themeMenu===null')
    def select_state(name):
        field('search',name)
        page.wait_for_function('name=>window.scadaSymbolEditor.rows?.length===1 && window.scadaSymbolEditor.rows[0].name===name && window.scadaSymbolEditor.rows[0].bounds?.width>0',arg=name,timeout=30000)
        click(editor()['rows'][0]['bounds']);wait('window.scadaSymbolEditor.selected==='+json.dumps(name))
    def runtime_state(name):
        box=page.evaluate("window.scadaViews[0].components.StateInput.bounds")
        click(box);page.keyboard.press('Control+a');page.keyboard.insert_text(name);page.keyboard.press('Enter')
    def export(name):
        with page.expect_download() as event: command('export')
        destination=OUT/name;event.value.save_as(destination);return json.loads(destination.read_text())
    def check(name, condition):
        results.append({'name':name,'passed':bool(condition)})
        if not condition: raise AssertionError(name)
        print('PASS',name,flush=True)
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000)
        page.wait_for_function('window.scadaApp?.ready && window.scadaApp.tick>2',timeout=120000)
        with page.expect_file_chooser() as chooser: command('import')
        chooser.value.set_files(fixture_file);wait("window.scadaApp.project==='Symbol State Lab'");command('fit');ticks()
        open_editor()
        check('Project menu opens the native state inventory and all five family previews',len(editor()['names'])==9 and len(editor()['previews'])==5)
        generation=app()['generation'];command('configuration-dialog-apply');ticks()
        check('opening and unchanged Apply do not dirty project history',app()['generation']==generation and not app()['dirty'])
        field('primary','red');command('configuration-dialog-apply');wait('window.scadaSymbolEditor.error.length>0')
        check('invalid state colors keep the dialog and project unchanged',app()['generation']==generation and not editor()['previewVisible'])
        field('primary','#A3469F');field('secondary','#DAB54D');field('stroke','#264F91');field('tertiary','#37A78E');field('flashing')
        command('configuration-dialog-apply');wait('window.scadaApp.generation>'+str(generation))
        check('Apply commits colors and independent flashing as one transaction',app()['generation']==generation+1 and editor()['state']['light']['enableFlashing'] and not editor()['state']['light']['enableAnimation'])
        wait('Object.values(window.scadaSymbolEditor.previews).every(s=>s.pictures>0 && !s.renderError)')
        check('designer previews display state materials with all effects paused',all(not s['animating'] and s['retainedPictures']==1 for s in editor()['previews'].values()))
        page.screenshot(path=str(OUT/'state-editor-light.png'),full_page=True)
        generation=app()['generation'];field('primary','#FFFFFF');command('configuration-dialog-cancel');wait('window.scadaSymbolEditor===null')
        open_editor();check('Cancel after Apply discards only later field edits',editor()['colors']['primary']['text']=='#A3469F' and app()['generation']==generation)
        command('configuration-dialog-cancel');wait('window.scadaSymbolEditor===null');command('undo');ticks()
        check('undo restores original state materials',symbol('Pump')['options']['stateStyle']['primaryColor']=='#F4F4F4' and not app()['dirty'])
        command('redo');ticks();check('redo restores the complete state override',symbol('Pump')['options']['stateStyle']['primaryColor']=='#A3469F')
        open_editor();field('new');wait("window.scadaSymbolEditor.selected==='new-state'")
        check('Add uses factory defaults and no applied symbol families',not editor()['state']['symbols'] and editor()['state']['light']['primaryColor']=='#F4F4F4' and not editor()['state']['light']['enableFlashing'])
        field('name','service');field('family-Motor');wait("window.scadaSymbolEditor.selected==='service'")
        field('primary','#4488BB');field('animation');choose_theme('dark');field('primary','#112244');field('animation')
        generation=app()['generation'];command('configuration-dialog-apply');wait('window.scadaApp.generation>'+str(generation))
        check('custom states own applied families and separate light-dark colors',editor()['state']['symbols']==['Motor'] and editor()['state']['light']['primaryColor']=='#4488BB' and editor()['state']['dark']['primaryColor']=='#112244')
        page.screenshot(path=str(OUT/'state-editor-dark.png'),full_page=True)
        field('primary','#778899');field('revert')
        check('Undo Theme restores the last applied style',editor()['colors']['primary']['text']=='#112244')
        field('reset');check('Reset Theme restores creation values for the selected theme',editor()['colors']['primary']['text']=='#343B41')
        field('revert');check('theme reset does not discard the saved revert baseline',editor()['colors']['primary']['text']=='#112244')
        field('duplicate');wait("window.scadaSymbolEditor.selected==='service-copy'")
        check('Duplicate carries selected values and applied families',editor()['state']['symbols']==['Motor'] and editor()['state']['dark']['primaryColor']=='#112244')
        field('delete');wait('window.scadaSymbolEditor.deleteConfirmation');field('delete-cancel')
        check('custom state deletion requires confirmation', 'service-copy' in editor()['names'] and not editor()['deleteConfirmation'])
        field('delete');wait('window.scadaSymbolEditor.deleteConfirmation');field('delete-confirm');wait("window.scadaSymbolEditor.selected==='default'")
        check('confirmed deletion removes only the selected custom state','service-copy' not in editor()['names'] and 'service' in editor()['names'] and 'default' in editor()['names'])
        select_state('service');field('search','');ticks()
        check('filter clearing restores the complete native inventory without losing selection',len(editor()['rows'])==10 and editor()['selected']=='service')
        command('configuration-dialog-ok');wait('window.scadaSymbolEditor===null')
        baseline=export('configured-project.json')
        check('portable export preserves both themes and only real overrides',set(baseline['symbolStates']['states'])=={'default','service'})
        command('preview');wait('window.scadaApp.preview');wait('window.scadaViews[0].components.Pump.symbol.retainedPictures===2')
        first=symbol('Pump')['alternateFill'];wait('window.scadaViews[0].components.Pump.symbol.alternateFill==='+str(not first).lower())
        check('flash-only runtime symbols display both retained color phases',symbol('Pump')['options']['isFlashing'] and not symbol('Pump')['options']['isAnimated'])
        before=symbol('Pump');started=time.monotonic();ticks(8);after=symbol('Pump');elapsed=time.monotonic()-started
        check('flash-only symbols invalidate at color transitions rather than every composition tick',0<after['animationFrames']-before['animationFrames']<=int(elapsed)+4 and after['pictures']==before['pictures'])
        page.screenshot(path=str(OUT/'state-runtime-light.png'),full_page=True)
        runtime_state('service');wait("window.scadaViews[0].components.Motor.symbol.options.state==='service' && window.scadaViews[0].components.Motor.symbol.retainedPictures===1")
        check('custom family states drive motion independently of flashing',symbol('Motor')['options']['stateStyle']['primaryColor']=='#4488BB' and symbol('Motor')['options']['isAnimated'] and not symbol('Motor')['options']['isFlashing'])
        identity=symbol('Motor')['id'];runtime_state('missing');wait('window.scadaViews[0].components.Motor.symbol.dataError!==null')
        check('unknown project state stops the affected symbol explicitly',not symbol('Motor')['animating'] and symbol('Pump')['dataError'] is None)
        rendered=symbol('Motor')['renders'];runtime_state('service')
        wait('window.scadaViews[0].components.Motor.symbol.dataError===null && window.scadaViews[0].components.Motor.symbol.renderError===null && window.scadaViews[0].components.Motor.symbol.renders>'+str(rendered))
        check('corrected project state recovers the same native control',symbol('Motor')['id']==identity)
        command('menu-Tools');command('symbol-library');wait('window.scadaApp.symbolLibrary!==null')
        click(visible_field('theme',True));wait("window.scadaApp.symbolLibrary.menus.theme?.some(row=>row.name==='dark' && row.bounds?.width>0)")
        click(next(row['bounds'] for row in app()['symbolLibrary']['menus']['theme'] if row['name']=='dark'))
        wait("window.scadaApp.symbolLibrary.defaults.theme==='dark'")
        click(visible_field('reduced',True));wait('window.scadaApp.symbolLibrary.defaults.reducedMotion')
        command('configuration-dialog-ok');wait('window.scadaApp.symbolLibrary===null');ticks()
        check('dark session theme resolves project colors without changing project defaults',symbol('Motor')['options']['stateStyle']['primaryColor']=='#112244' and app()['symbolDefaults']['theme']=='dark')
        check('reduced motion disables both flashing and mechanical effects',not symbol('Pump')['animating'] and not symbol('Motor')['animating'] and symbol('Pump')['retainedPictures']==1)
        check('runtime state and theme choices leave the complete portable project unchanged',export('after-runtime.json')==baseline)
        command('preview');wait('!window.scadaApp.preview');open_editor();select_state('service');choose_theme('dark')
        field('name','maintenance');field('primary','#998877');choose_theme('light');choose_theme('dark');field('revert')
        check('Undo Theme follows state renames to the last applied values',editor()['selected']=='maintenance' and editor()['colors']['primary']['text']=='#112244')
        command('configuration-dialog-cancel');wait('window.scadaSymbolEditor===null')
        check('cancelled state rename does not leak into exported catalog',export('after-cancel.json')==baseline)
        page.set_viewport_size({'width':1024,'height':768});ticks(3);open_editor();field('primary','#ABCDEF');field('revert')
        check('tablet state editor retains native scrolling and theme editing',editor()['error']=='' and editor()['colors']['primary']['text']=='#A3469F')
        page.screenshot(path=str(OUT/'state-editor-tablet.png'),full_page=True)
        command('configuration-dialog-cancel');wait('window.scadaSymbolEditor===null');command('fit');ticks()
        check('all state workflows complete without unhandled application or browser errors',not app()['errors'] and not app()['diagnostics'] and not any(e['kind']=='pageerror' for e in logs))
    except Exception as error:
        results.append({'name':'symbol state browser run','passed':False,'error':str(error)})
        page.screenshot(path=str(OUT/'failure.png'),full_page=True);raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,editor:window.scadaSymbolEditor,views:window.scadaViews})'),indent=2));browser.close()
