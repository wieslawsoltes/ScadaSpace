"""Flat property filtering with real native input, retained editors and portable-project checks."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright
from native_readiness import wait_command_bounds, wait_component_sample

OUT=Path('TestResults/browser-inspector-search');OUT.mkdir(parents=True,exist_ok=True)
BASE=os.environ.get('SCADASPACE_URL','http://127.0.0.1:8765/ScadaSpace/')
results,logs=[],[]
def scalar(value):
    if isinstance(value,bool):return {'kind':'Boolean','boolean':value}
    if isinstance(value,(int,float)):return {'kind':'Number','number':value}
    return {'kind':'String','text':value}
def node(name,kind,x,y,w,h,**props):
    return {'id':name,'name':name,'type':kind,'x':x,'y':y,'width':w,'height':h,'props':{k:scalar(v) for k,v in props.items()}}
root=node('Root','Container',0,0,1000,640);root['custom']={'lit':scalar('#2288CC')}
led=node('Led','LEDDisplay',25,90,360,100,value=42.6,segmentFormat='14 segment',numberFormat='0.0',backgroundColor='#161616',diodeOnColor='#1EC963',diodeOffColor='#193B29')
led['custom']={'note':scalar('baseline'),'colorRecipe':scalar('original')}
bound=node('BoundLed','LEDDisplay',560,430,350,100,value=12.34,segmentFormat='14 segment',numberFormat='0.00',backgroundColor='#161616',diodeOnColor='#2288CC')
bound['bindings']={'props.diodeOnColor':{'kind':'Property','source':'root.custom.lit'}}
root['children']=[node('Title','Label',20,10,950,40,text='PROPERTY SEARCH · retained native editors',fontSize=23),led,
    node('Vessel','Vessel',480,75,220,230,value=45,state='default',appearance='mimic',orientation='vertical',liquidColor='#00ACAC',displayStand=True,displayAgitator=True,displayFillLevel=True),bound]
fixture={'schemaVersion':1,'name':'Property Search Lab','views':[{'path':'Main','width':1000,'height':640,'root':root}],'pages':[{'path':'/','view':'Main'}],'tags':[],'alarms':[]}
fixture_file=OUT/'fixture.json';fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1700,'height':1100},accept_downloads=True);page=context.new_page()
    page.on('pageerror',lambda e:logs.append({'kind':'pageerror','text':str(e)}));page.on('console',lambda e:logs.append({'kind':e.type,'text':e.text}))
    def app():return page.evaluate('window.scadaApp')
    def state():return app()['inspectorFilter']
    def wait(js):page.wait_for_function(js,timeout=30000)
    def click(b):
        assert b and b['width']>0 and b['height']>0,'Expected arranged native control'
        page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
    def command(key):click(wait_command_bounds(page,key))
    def filter_command(key):
        page.wait_for_function('k=>window.scadaApp.inspectorFilter?.controls[k]?.width>0',arg=key,timeout=30000)
        click(state()['controls'][key])
    def search(text):
        filter_command('search');page.keyboard.press('Control+a');page.keyboard.insert_text(text)
        page.wait_for_function('q=>window.scadaApp.inspectorFilter.query.toLowerCase()===q.trim().toLowerCase()',arg=text,timeout=30000)
    def select(name):
        sample=wait_component_sample(page,name);logs.append({'kind':'component-input','name':name,**sample});click(sample['bounds'])
        page.wait_for_function('n=>window.scadaApp.selection[0]===n && window.scadaApp.inspectorFilter.total>0',arg=name,timeout=30000)
    def edit(key,text):
        page.wait_for_function('k=>window.scadaApp.inspector[k]?.width>0',arg=key,timeout=30000)
        click(app()['inspector'][key]);page.keyboard.press('Control+a');page.keyboard.insert_text(text);page.keyboard.press('Enter')
    def check(name,ok):
        results.append({'name':name,'passed':bool(ok)})
        if not ok:raise AssertionError(name)
        print('PASS',name,flush=True)
    def export(name):
        with page.expect_download() as event:command('export')
        path=OUT/name;event.value.save_as(path);return json.loads(path.read_text())
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000);page.wait_for_function('window.scadaApp?.ready',timeout=120000)
        with page.expect_file_chooser() as event:command('import')
        event.value.set_files(fixture_file);wait("window.scadaApp.project==='Property Search Lab'");command('fit');select('Led')
        initial=export('initial.json');generation=app()['generation'];before=state()
        check('native filter bar exposes search clear and bound-only controls',all(state()['controls'][k] and state()['controls'][k]['width']>0 for k in ['search','clear','bound']))
        check('empty filtering retains every flat property row',len(state()['visible'])==state()['total'] and not state()['noMatches'])
        search('COLOR');wait('window.scadaApp.inspector.name===null')
        check('literal case-insensitive search includes matching props and custom names',set(state()['visible'])=={'props.backgroundColor','props.diodeOnColor','props.diodeOffColor','custom.colorRecipe'})
        check('searching hides unrelated native editors instead of rebuilding them',state()['rowsCreated']==before['rowsCreated'] and state()['builds']==before['builds'] and app()['generation']==generation)
        search('props.diode');check('qualified name filtering distinguishes section and property',set(state()['visible'])=={'props.diodeOnColor','props.diodeOffColor'})
        search('no-such-property');wait('window.scadaApp.inspectorFilter.noMatches')
        check('unmatched searches expose an explicit empty state and hide all fields',state()['visible']==[] and all(value is None for value in app()['inspector'].values()))
        page.keyboard.press('Escape');wait("window.scadaApp.inspectorFilter.query==='' && !window.scadaApp.inspectorFilter.boundOnly")
        check('Escape restores the retained property controls without editing the document',len(state()['visible'])==state()['total'] and state()['rowsCreated']==before['rowsCreated'] and app()['generation']==generation)
        search('props.backgroundColor');edit('backgroundColor','#304050')
        wait("window.scadaViews[0].components.Led.symbol.led.options.backgroundColor==='#304050'")
        check('filtered native color fields still commit ordinary undoable edits',app()['generation']==generation+1 and state()['query']=='props.backgroundColor' and state()['visible']==['props.backgroundColor'])
        command('undo');wait("window.scadaViews[0].components.Led.symbol.led.options.backgroundColor==='#161616'")
        check('undo restores edited colors while preserving the local search',not app()['dirty'] and state()['query']=='props.backgroundColor')
        search('custom.note');generation=app()['generation'];edit('note','reviewed')
        wait('window.scadaApp.generation>'+str(generation))
        current=export('custom-edited.json');saved=next(n for n in current['views'][0]['root']['children'] if n['name']=='Led')
        check('filtered custom properties use the existing validated transaction',saved['custom']['note']['text']=='reviewed' and state()['visible']==['custom.note'])
        command('undo');wait('!window.scadaApp.dirty');filter_command('clear');wait("window.scadaApp.inspectorFilter.query===''")
        filter_command('bound');wait('window.scadaApp.inspectorFilter.boundOnly')
        check('bound-only filtering does not display unrelated unbound editors',state()['visible']==[] and state()['noMatches'])
        select('BoundLed');wait("window.scadaApp.inspectorFilter.visible.includes('props.diodeOnColor')")
        check('selection changes retain the filter and recognize qualified bindings',state()['boundOnly'] and state()['visible']==['props.diodeOnColor'])
        check('filtered bound material values remain read-only and evaluated',not app()['inspectorTyped']['colors']['diodeOnColor']['enabled'] and app()['inspectorTyped']['colors']['diodeOnColor']['text']=='#2288CC')
        search('background');check('bound-only and text filters are combined rather than alternatives',state()['visible']==[])
        filter_command('clear');wait("window.scadaApp.inspectorFilter.query==='' && !window.scadaApp.inspectorFilter.boundOnly")
        check('the clear command resets both filters',len(state()['visible'])==state()['total'])
        select('Vessel');search('display');check('native Boolean rows participate in the same flat property search',len(state()['visible'])==3 and all(path.startswith('props.display') for path in state()['visible']))
        page.wait_for_function('window.scadaApp.inspectorTyped.switches.displayFillLevel.bounds?.width>0',timeout=30000)
        b=app()['inspectorTyped']['switches']['displayFillLevel']['bounds'];page.mouse.click(b['x']+12,b['y']+b['height']/2)
        wait('window.scadaViews[0].components.Vessel.symbol.options.displayFillLevel===false')
        check('filtered Boolean editors retain ordinary renderer updates and search state',state()['query']=='display' and app()['canUndo'])
        command('undo');wait('window.scadaViews[0].components.Vessel.symbol.options.displayFillLevel===true && !window.scadaApp.dirty')
        generation=app()['generation'];count=app()['componentCount'];search('delete guard');page.keyboard.press('Control+a');page.keyboard.press('Delete')
        wait("window.scadaApp.inspectorFilter.query===''")
        check('Delete in property search edits text rather than deleting canvas selection',app()['generation']==generation and app()['componentCount']==count and app()['selection']==['Vessel'])
        select('Led');search('color');idle=state();tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+3))
        check('idle inspector refreshes do not rebuild rows or reapply an unchanged filter',state()['rowsCreated']==idle['rowsCreated'] and state()['builds']==idle['builds'] and state()['applies']==idle['applies'])
        page.screenshot(path=str(OUT/'filtered-materials-desktop.png'),full_page=True)
        page.set_viewport_size({'width':1024,'height':768});wait('window.scadaApp.viewport.width===1024');command('fit');select('Led')
        search('props.diode');page.wait_for_function('window.scadaApp.inspector.diodeOnColor?.width>0',timeout=30000)
        check('property search and retained native color fields remain usable on a tablet',set(state()['visible'])=={'props.diodeOnColor','props.diodeOffColor'} and state()['controls']['search']['width']>0)
        page.screenshot(path=str(OUT/'filtered-materials-tablet.png'),full_page=True)
        check('searching filtering and inspection leave the complete exported project unchanged',export('final.json')==initial and not app()['dirty'])
        check('property search workflows finish without unhandled errors',not app()['errors'] and not app()['diagnostics'] and not any(row['kind']=='pageerror' for row in logs))
    except Exception as error:
        results.append({'name':'property search suite','passed':False,'error':str(error)});page.screenshot(path=str(OUT/'failure.png'),full_page=True);raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'),indent=2));browser.close()
