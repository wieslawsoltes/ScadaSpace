"""Native flat color and Boolean property authoring. All mutations use normal UI input."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright
from native_readiness import wait_command_bounds

OUT=Path('TestResults/browser-symbol-colors');OUT.mkdir(parents=True,exist_ok=True)
BASE=os.environ.get('SCADASPACE_URL','http://127.0.0.1:8765/ScadaSpace/')
results,logs=[],[]
def scalar(value):
    if isinstance(value,bool):return {'kind':'Boolean','boolean':value}
    if isinstance(value,(int,float)):return {'kind':'Number','number':value}
    return {'kind':'String','text':value}
def node(name,kind,x,y,w,h,**props):
    return {'id':name,'name':name,'type':kind,'x':x,'y':y,'width':w,'height':h,'props':{k:scalar(v) for k,v in props.items()}}
root=node('Root','Container',0,0,1000,640)
root['custom']={'lit':scalar('#2288CC'),'fill':scalar(True)}
root['children']=[
    node('Title','Label',20,10,950,40,text='SYMBOL MATERIALS · native colors and flags',fontSize=23),
    node('Led','LEDDisplay',25,90,360,100,value=42.6,segmentFormat='14 segment',numberFormat='0.0',backgroundColor='#161616',diodeOnColor='#1EC963',diodeOffColor='#193B29'),
    node('Vessel','Vessel',440,75,220,230,value=45,state='default',appearance='mimic',orientation='vertical',liquidColor='#00ACAC',displayStand=True,displayAgitator=True,displayFillLevel=True),
    node('Tank','Tank',710,75,220,230,value=45,color='#48AFC0',tankColor='#D6D6D6',showScale=True),
    node('Shape','Rectangle',30,390,210,180,color='#5599BB',stroke='#335577'),
    node('Valve','Valve',300,370,210,200,value=1,state='open',appearance='simple',orientation='right',reverseFlow=False),
    node('BoundLed','LEDDisplay',580,440,350,100,value=12.34,segmentFormat='14 segment',numberFormat='0.00',diodeOnColor='#2288CC')
]
root['children'][-1]['bindings']={'props.diodeOnColor':{'kind':'Property','source':'root.custom.lit'}}
fixture={'schemaVersion':1,'name':'Symbol Color Lab','views':[{'path':'Main','width':1000,'height':640,'root':root}],'pages':[{'path':'/','view':'Main'}],'tags':[],'alarms':[]}
fixture_file=OUT/'fixture.json';fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1700,'height':1100},accept_downloads=True);page=context.new_page()
    page.on('pageerror',lambda e:logs.append({'kind':'pageerror','text':str(e)}));page.on('console',lambda e:logs.append({'kind':e.type,'text':e.text}))
    def app():return page.evaluate('window.scadaApp')
    def typed():return app()['inspectorTyped']
    def symbol(name):return page.evaluate('n=>window.scadaViews[0].components[n].symbol',name)
    def wait(js):page.wait_for_function(js,timeout=30000)
    def tick():
        t=app()['tick'];wait('window.scadaApp.tick>'+str(t+2))
    def click(b):
        assert b and b['width']>0 and b['height']>0,'Expected arranged native bounds'
        page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
    def command(key):click(wait_command_bounds(page,key))
    def select(name):
        page.wait_for_function('n=>window.scadaViews?.[0]?.components[n]?.bounds?.width>0',arg=name,timeout=30000)
        click(page.evaluate('n=>window.scadaViews[0].components[n].bounds',name))
        page.wait_for_function('n=>window.scadaApp.selection[0]===n && window.scadaApp.inspector.name?.width>0',arg=name,timeout=30000)
    def aux(key,control,text=None):
        page.wait_for_function('p=>window.scadaApp.inspectorTyped.colors[p.key]?.controls[p.control]?.width>0',arg={'key':key,'control':control},timeout=30000)
        click(typed()['colors'][key]['controls'][control])
        if text is not None:
            page.keyboard.press('Control+a');page.keyboard.press('Backspace');page.keyboard.insert_text(text);tick()
    def open_color(key):
        aux(key,'color');page.wait_for_function('k=>window.scadaApp.inspectorTyped.colors[k].open',arg=key,timeout=30000)
    def text_color(key,text):
        page.wait_for_function('k=>window.scadaApp.inspector[k]?.width>0',arg=key,timeout=30000)
        click(app()['inspector'][key]);page.keyboard.press('Control+a');page.keyboard.press('Backspace');page.keyboard.insert_text(text);page.keyboard.press('Enter')
    def toggle(key):
        page.wait_for_function('k=>window.scadaApp.inspectorTyped.switches[k]?.bounds?.width>0',arg=key,timeout=30000)
        b=typed()['switches'][key]['bounds']
        # Hit the checkbox indicator, not the empty stretch area of its grid cell.
        page.mouse.click(b['x']+12,b['y']+b['height']/2)
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
        event.value.set_files(fixture_file);wait("window.scadaApp.project==='Symbol Color Lab'");command('fit')
        select('Led');wait('Object.keys(window.scadaApp.inspectorTyped.colors).length===3 && Object.values(window.scadaApp.inspectorTyped.colors).every(v=>v.controls.color?.width>0)')
        check('LED materials expose three native swatches without inheritance reset controls',all(v['controls']['color'] is not None and v['controls']['reset'] is None for v in typed()['colors'].values()))
        generation=app()['generation'];open_color('diodeOnColor');aux('diodeOnColor','color-text','#80446688');aux('diodeOnColor','color-cancel')
        wait('!window.scadaApp.inspectorTyped.colors.diodeOnColor.open')
        check('Cancel discards picker changes without editing project history',app()['generation']==generation and typed()['colors']['diodeOnColor']['text']=='#1EC963')
        open_color('diodeOnColor');aux('diodeOnColor','color-text','#80GG6688');aux('diodeOnColor','color-use')
        wait('window.scadaApp.inspectorTyped.colors.diodeOnColor.error.length>0')
        check('invalid picker colors cannot become process display materials',typed()['colors']['diodeOnColor']['open'] and app()['generation']==generation and symbol('Led')['led']['options']['diodeOnColor']=='#1EC963')
        aux('diodeOnColor','color-text','#80446688');aux('diodeOnColor','color-use')
        wait("window.scadaApp.generation>"+str(generation)+" && window.scadaViews[0].components.Led.symbol.led.options.diodeOnColor==='#80446688'")
        check('Use color applies alpha through one undoable native edit',app()['generation']==generation+1 and app()['canUndo'])
        command('undo');wait("window.scadaViews[0].components.Led.symbol.led.options.diodeOnColor==='#1EC963'")
        check('color Undo restores both the readout and original portable color spelling',not app()['dirty'])
        command('redo');wait("window.scadaViews[0].components.Led.symbol.led.options.diodeOnColor==='#80446688'")
        check('color Redo restores the complete alpha-aware material',symbol('Led')['led']['options']['diodeOnColor']=='#80446688')
        generation=app()['generation'];text_color('backgroundColor','#FF161616');tick()
        check('equivalent RGB and ARGB values do not add redundant history',app()['generation']==generation and typed()['colors']['backgroundColor']['text']=='#161616')
        text_color('backgroundColor','red');tick()
        check('invalid direct color text is rejected before publication',app()['generation']==generation and typed()['colors']['backgroundColor']['text']=='#161616')
        select('Vessel');wait('window.scadaApp.inspectorTyped.switches.displayFillLevel!==undefined')
        check('vessel flags use native checkboxes and its liquid has a swatch',len(typed()['switches'])==3 and 'liquidColor' in typed()['colors'])
        toggle('displayFillLevel');wait('window.scadaViews[0].components.Vessel.symbol.options.displayFillLevel===false')
        check('fill checkbox updates the actual vessel renderer',not symbol('Vessel')['options']['displayFillLevel'])
        command('undo');wait('window.scadaViews[0].components.Vessel.symbol.options.displayFillLevel===true')
        generation=app()['generation'];text_color('liquidColor','#DD7711')
        wait("window.scadaViews[0].components.Vessel.symbol.options.liquidColor==='#DD7711'")
        check('liquid swatches retain the component override rather than changing project state palettes',app()['generation']==generation+1 and not symbol('Vessel')['options']['useStateLiquidColor'])
        select('Tank');wait('window.scadaApp.inspectorTyped.switches.showScale!==undefined');toggle('showScale')
        wait('window.scadaViews[0].components.Tank.symbol.tank.showScale===false')
        check('tank scale checkbox updates its independent display options',not symbol('Tank')['tank']['showScale'])
        select('Valve');wait('window.scadaApp.inspectorTyped.switches.reverseFlow!==undefined');toggle('reverseFlow')
        wait('window.scadaViews[0].components.Valve.symbol.options.reverseFlow===true')
        check('valve flow direction has an independent native Boolean editor',symbol('Valve')['options']['reverseFlow'])
        select('Shape');wait('Object.keys(window.scadaApp.inspectorTyped.colors).length===2');text_color('stroke','#CC3311')
        wait("window.scadaApp.inspectorTyped.colors.stroke.text==='#CC3311'")
        check('drawing primitives expose their actual fill and outline color inputs','color' in typed()['colors'] and 'stroke' in typed()['colors'])
        select('BoundLed');wait('window.scadaApp.inspectorTyped.colors.diodeOnColor!==undefined');generation=app()['generation']
        check('qualified bound materials remain read-only and keep their evaluated colors',not typed()['colors']['diodeOnColor']['enabled'] and typed()['colors']['diodeOnColor']['text']=='#2288CC')
        tick();check('inspecting live bound material inputs does not modify the document',app()['generation']==generation)
        select('Led');page.screenshot(path=str(OUT/'typed-material-editor.png'),full_page=True)
        baseline=export('configured.json');command('preview');wait('window.scadaApp.preview')
        check('previewing edited materials leaves the entire exported project unchanged',export('runtime.json')==baseline)
        command('preview');wait('!window.scadaApp.preview')
        page.set_viewport_size({'width':1024,'height':768});wait('window.scadaApp.viewport.width===1024');command('fit');select('Led')
        text_color('diodeOffColor','#334455');wait("window.scadaViews[0].components.Led.symbol.led.options.diodeOffColor==='#334455'")
        check('typed material controls remain usable at tablet dimensions',symbol('Led')['renderError'] is None and symbol('Led')['dataError'] is None)
        page.screenshot(path=str(OUT/'typed-material-tablet.png'),full_page=True)
        check('native material workflows finish without unhandled errors',not app()['errors'] and not app()['diagnostics'] and not any(e['kind']=='pageerror' for e in logs))
    except Exception as error:
        results.append({'name':'typed symbol material suite','passed':False,'error':str(error)});page.screenshot(path=str(OUT/'failure.png'),full_page=True);raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'),indent=2));browser.close()
