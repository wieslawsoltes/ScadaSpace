"""Native search modes, retained category folding and qualified editor regressions."""
from __future__ import annotations
import json
import os
import time
from pathlib import Path
from playwright.sync_api import sync_playwright
from native_readiness import CommandReadiness, wait_command_bounds, wait_component_sample

OUT=Path('TestResults/browser-inspector-search-modes');OUT.mkdir(parents=True,exist_ok=True)
BASE=os.environ.get('SCADASPACE_URL','http://127.0.0.1:8765/ScadaSpace/')
results,logs=[],[]
def scalar(v):
    if isinstance(v,bool):return {'kind':'Boolean','boolean':v}
    if isinstance(v,(int,float)):return {'kind':'Number','number':v}
    return {'kind':'String','text':v}
def node(name,kind,x,y,w,h,**props):
    return {'id':name,'name':name,'type':kind,'x':x,'y':y,'width':w,'height':h,'props':{k:scalar(v) for k,v in props.items()}}
root=node('Root','Container',0,0,1000,640);root['custom']={'lit':scalar('#2288CC')}
led=node('Led','LEDDisplay',25,100,360,110,value=42.6,segmentFormat='14 segment',numberFormat='0.0',backgroundColor='#161616',diodeOnColor='#1EC963',diodeOffColor='#193B29')
led['custom']={k:scalar(v) for k,v in {'name':'independent metadata','backgroundColor':'recipe','colorRecipe':'original','port7':7,'note':'baseline'}.items()}
bound=node('BoundLed','LEDDisplay',560,430,350,100,value=12.34,segmentFormat='14 segment',numberFormat='0.00',backgroundColor='#161616',diodeOnColor='#2288CC')
bound['bindings']={'props.diodeOnColor':{'kind':'Property','source':'root.custom.lit'}}
root['children']=[node('Title','Label',20,10,950,40,text='PROPERTY EDITOR · search modes and categories',fontSize=23),led,
    node('Vessel','Vessel',480,75,220,230,value=45,state='default',appearance='mimic',orientation='vertical',liquidColor='#00ACAC',displayStand=True,displayAgitator=True,displayFillLevel=True),bound]
fixture={'schemaVersion':1,'name':'Property Mode Lab','views':[{'path':'Main','width':1000,'height':640,'root':root}],'pages':[{'path':'/','view':'Main'}],'tags':[],'alarms':[]}
fixture_file=OUT/'fixture.json';fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1700,'height':1100},accept_downloads=True);page=context.new_page()
    page.on('pageerror',lambda e:logs.append({'kind':'pageerror','text':str(e)}));page.on('console',lambda e:logs.append({'kind':e.type,'text':e.text}))
    def app():return page.evaluate('window.scadaApp')
    def state():return app()['inspectorFilter']
    def wait(js):page.wait_for_function(js,timeout=30000)
    def click(b):
        assert b and b['width']>0 and b['height']>0,'Expected native control bounds'
        page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
    def bounds(path):
        tracker=CommandReadiness();deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            sample=page.evaluate('''path=>{let b=window.scadaApp;for(const key of path)b=b?.[key];return {
                tick:window.scadaApp.tick,project:window.scadaApp.project,viewId:window.scadaViews?.[0]?.id,
                viewport:window.scadaApp.viewport,bounds:b};}''',path)
            if tracker.accept(sample):return sample['bounds']
            page.wait_for_function('t=>window.scadaApp.tick>t',arg=sample['tick'],timeout=30000)
        raise TimeoutError('Native bounds did not settle: '+str(path))
    def command(key):click(wait_command_bounds(page,key))
    def control(key):click(bounds(['inspectorFilter','controls',key]))
    def option(key):
        control('options');wait('window.scadaApp.inspectorFilter.options!==null')
        click(bounds(['inspectorFilter','options',key,'bounds']));wait('window.scadaApp.inspectorFilter.options===null')
    def search(text):
        control('search');page.keyboard.press('Control+a');page.keyboard.press('Backspace')
        if text:page.keyboard.insert_text(text)
        page.wait_for_function('q=>window.scadaApp.inspectorFilter.query===q',arg=text,timeout=30000)
    def clear():control('clear');wait("window.scadaApp.inspectorFilter.query==='' && !window.scadaApp.inspectorFilter.boundOnly")
    def category(key):click(bounds(['inspectorFilter','headers',key]))
    def select(name):
        sample=wait_component_sample(page,name);logs.append({'kind':'component-input','name':name,**sample});click(sample['bounds'])
        page.wait_for_function('n=>window.scadaApp.selection[0]===n && window.scadaApp.inspectorFilter.total>0',arg=name,timeout=30000)
    def edit(path,text):
        click(bounds(['inspectorQualified',path]));page.keyboard.press('Control+a');page.keyboard.insert_text(text);page.keyboard.press('Enter')
    def check(name,value):
        results.append({'name':name,'passed':bool(value)})
        if not value:raise AssertionError(name)
        print('PASS',name,flush=True)
    def export(name):
        with page.expect_download() as event:command('export')
        file=OUT/name;event.value.save_as(file);return json.loads(file.read_text())
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000);page.wait_for_function('window.scadaApp?.ready',timeout=120000)
        with page.expect_file_chooser() as event:command('import')
        event.value.set_files(fixture_file);wait("window.scadaApp.project==='Property Mode Lab'");command('fit');select('Led')
        initial=export('initial.json');generation=app()['generation'];before=state()
        control('options');wait('window.scadaApp.inspectorFilter.options!==null');bounds(['inspectorFilter','options','reset','bounds'])
        menu=state()['options'];check('native search options expose every syntax boundary case and category command',len(menu)==10 and menu['literal']['selected'] and menu['anywhere']['selected'] and not menu['case']['selected'])
        page.keyboard.press('Escape');wait('window.scadaApp.inspectorFilter.options===null')
        option('start');search('diode')
        check('prefix mode matches real property names instead of requiring a section prefix',set(state()['visible'])=={'props.diodeOnColor','props.diodeOffColor'} and state()['mode']=='StartsWith')
        search('Color');check('prefix mode excludes suffix-only matches',state()['visible']==['custom.colorRecipe'])
        option('exact');search('backgroundColor')
        check('exact matching keeps identically named properties in separate categories',set(state()['visible'])=={'props.backgroundColor','custom.backgroundColor'})
        a=bounds(['inspectorQualified','props.backgroundColor']);b=bounds(['inspectorQualified','custom.backgroundColor'])
        check('qualified native editor observations do not overwrite duplicate names',a['y']!=b['y'])
        edit('custom.backgroundColor','changed recipe');wait('window.scadaApp.generation>'+str(generation))
        saved=export('custom-edit.json');saved_led=next(n for n in saved['views'][0]['root']['children'] if n['name']=='Led')
        check('editing a colliding custom name leaves the actual symbol material unchanged',saved_led['custom']['backgroundColor']['text']=='changed recipe' and saved_led['props']['backgroundColor']['text']=='#161616' and saved_led['name']=='Led')
        command('undo');wait('!window.scadaApp.dirty');search('props.backgroundColor')
        check('exact matching also accepts an explicit qualified property path',state()['visible']==['props.backgroundColor'])
        option('case');search('props.backgroundcolor')
        check('case-sensitive mode does not silently fold property name case',state()['noMatches'] and state()['caseSensitive'])
        search('props.backgroundColor');check('case-sensitive searches recover without changing the document',state()['visible']==['props.backgroundColor'] and not app()['dirty'])
        option('case');option('wildcard');search('diode?*Color')
        check('wildcards use the native mode selection and honor full-name matching',set(state()['visible'])=={'props.diodeOnColor','props.diodeOffColor'} and state()['syntax']=='Wildcard')
        option('regex');search('^diode(On|Off)Color$')
        check('regular expressions match the real flat property names',set(state()['visible'])=={'props.diodeOnColor','props.diodeOffColor'} and state()['error']=='')
        generation=app()['generation'];search('[')
        check('invalid regex reports an error and hides the entire stale projection',state()['error']!='' and not state()['visible'] and all(v is None for v in app()['inspectorQualified'].values()) and app()['generation']==generation)
        option('anywhere');search('\\d');wait("window.scadaApp.inspectorFilter.visible.includes('custom.port7')")
        check('digit regex finds the numeric property name',state()['visible']==['custom.port7'])
        builds=state()['patternBuilds'];search('\\D')
        check('changing regex escape case recompiles rather than becoming an ignore-case no-op',state()['patternBuilds']==builds+1 and 'meta.name' in state()['visible'] and len(state()['visible'])>1)
        builds=state()['patternBuilds'];search('a{1000000000}')
        check('repetition bombs are rejected before constructing a regex',state()['error']!='' and state()['patternBuilds']==builds and not state()['visible'])
        search('^diodeOnColor$');control('bound');wait('window.scadaApp.inspectorFilter.boundOnly')
        select('BoundLed');wait("window.scadaApp.inspectorFilter.visible.includes('props.diodeOnColor')")
        check('regex and bound-only filtering compose across selection changes',state()['visible']==['props.diodeOnColor'] and not app()['inspectorTyped']['colors']['diodeOnColor']['enabled'])
        clear();check('Clear resets text and binding mode while retaining the chosen search syntax',state()['syntax']=='RegularExpression' and not state()['boundOnly'] and len(state()['visible'])==state()['total'])
        option('reset');select('Led');wait("window.scadaApp.inspectorFilter.syntax==='Literal'")
        before=state();category('props');wait('!window.scadaApp.inspectorFilter.categories.props.expanded')
        check('category folding retains controls and the cached match projection',all(not v.startswith('props.') for v in state()['visible']) and len(state()['matches'])==state()['total'] and state()['rowsCreated']==before['rowsCreated'] and state()['applies']==before['applies'])
        page.keyboard.press('ArrowRight');wait('window.scadaApp.inspectorFilter.categories.props.expanded')
        page.keyboard.press('ArrowLeft');wait('!window.scadaApp.inspectorFilter.categories.props.expanded')
        generation=app()['generation'];count=app()['componentCount'];page.keyboard.press('Delete');tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+2))
        check('category keyboard navigation and Delete cannot move or delete canvas selection',app()['generation']==generation and app()['componentCount']==count and app()['selection']==['Led'])
        search('diode');check('search reveals matches inside an ordinarily collapsed category',state()['categories']['props']['expanded'] and len(state()['visible'])==2)
        clear();check('clearing search restores the preceding category fold preference',not state()['categories']['props']['expanded'] and not state()['noMatches'])
        select('Vessel');check('ordinary category fold preferences survive component selection changes',not state()['categories']['props']['expanded'])
        select('Led');option('collapse');wait('window.scadaApp.inspectorFilter.visible.length===0')
        check('Collapse All preserves category headers and is not reported as no search results',not state()['noMatches'] and len(state()['matches'])==state()['total'] and all(c['visible'] and not c['expanded'] for c in state()['categories'].values()))
        search('color');category('custom');wait('!window.scadaApp.inspectorFilter.categories.custom.expanded')
        search('note');check('a new query reveals matching categories without rebuilding their editors',state()['visible']==['custom.note'] and state()['categories']['custom']['expanded'])
        clear();option('expand');wait('window.scadaApp.inspectorFilter.visible.length===window.scadaApp.inspectorFilter.total')
        search('props.backgroundColor');generation=app()['generation'];edit('props.backgroundColor','#304050')
        wait("window.scadaViews[0].components.Led.symbol.led.options.backgroundColor==='#304050'")
        check('mode-aware filtering retains real native material editing transactions',app()['generation']==generation+1 and state()['query']=='props.backgroundColor')
        command('undo');wait("window.scadaViews[0].components.Led.symbol.led.options.backgroundColor==='#161616' && !window.scadaApp.dirty")
        option('wildcard');option('exact');search('*Color');idle=state();tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+3))
        check('idle refreshes neither rebuild editors nor reapply search or compile patterns',all(state()[k]==idle[k] for k in ['rowsCreated','builds','applies','patternBuilds']))
        control('options');wait('window.scadaApp.inspectorFilter.options!==null');bounds(['inspectorFilter','options','reset','bounds']);page.screenshot(path=str(OUT/'search-options-desktop.png'),full_page=True)
        page.keyboard.press('Escape');wait('window.scadaApp.inspectorFilter.options===null')
        page.set_viewport_size({'width':1024,'height':768});wait('window.scadaApp.viewport.width===1024');command('fit');select('Led')
        option('start');search('diode');option('exact');search('diode*Color')
        check('native search options remain reachable in the tablet layout',state()['syntax']=='Wildcard' and set(state()['visible'])=={'props.diodeOnColor','props.diodeOffColor'})
        edit('props.diodeOffColor','#334455');wait("window.scadaViews[0].components.Led.symbol.led.options.diodeOffColor==='#334455'")
        check('tablet mode-filtered property controls still drive the actual renderer',app()['canUndo'] and state()['query']=='diode*Color')
        command('undo');wait("window.scadaViews[0].components.Led.symbol.led.options.diodeOffColor==='#193B29' && !window.scadaApp.dirty")
        page.screenshot(path=str(OUT/'search-categories-tablet.png'),full_page=True)
        check('search modes category folding and reverted edits preserve the complete portable project',export('final.json')==initial)
        check('native search mode workflows finish without unhandled errors',not app()['errors'] and not app()['diagnostics'] and not any(row['kind']=='pageerror' for row in logs))
    except Exception as error:
        results.append({'name':'search modes suite','passed':False,'error':str(error)});page.screenshot(path=str(OUT/'failure.png'),full_page=True);raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'),indent=2));browser.close()
