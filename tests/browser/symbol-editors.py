"""Native flat symbol property choices; the project and renderer provide the observations."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright
from native_readiness import wait_command_bounds

OUT=Path('TestResults/browser-symbol-editors');OUT.mkdir(parents=True,exist_ok=True)
BASE=os.environ.get('SCADASPACE_URL','http://127.0.0.1:8765/ScadaSpace/')
results,logs=[],[]
def scalar(value):
    if isinstance(value,bool):return {'kind':'Boolean','boolean':value}
    if isinstance(value,(float,int)):return {'kind':'Number','number':value}
    return {'kind':'String','text':value}
def node(name,kind,x,y,w,h,**props):
    return {'id':name,'name':name,'type':kind,'x':x,'y':y,'width':w,'height':h,'props':{k:scalar(v) for k,v in props.items()}}
root=node('Root','Container',0,0,1000,640)
root['custom']={'state':scalar('default')}
root['children']=[node('Title','Label',20,10,940,40,text='SYMBOL PROPERTY EDITORS · family-aware native choices',fontSize=23)]
for index,kind in enumerate(['Pump','Motor','Valve','Sensor','Vessel']):
    root['children'].append(node(kind,kind,20+index*195,85,170,230,value=45,appearance='auto',state='default',orientation='vertical' if kind=='Vessel' else 'right',variant='centrifugal',feet='bottom',label=kind))
bound=node('BoundPump','Pump',25,370,200,220,value=45,appearance='simple',state='default',orientation='right',label='Bound state')
bound['bindings']={'props.state':{'kind':'Property','source':'root.custom.state'}}
root['children'] += [bound,
    node('InvalidMotor','Motor',285,370,200,220,value=45,appearance='simple',state='missing',orientation='right',label='Repair invalid state'),
    node('Pipe','Pipe',580,455,330,75,value=True,route='elbow',strokeWidth=12)]
style={'primaryColor':'#67A9CC','secondaryColor':'#4488AA','tertiaryColor':'#00ACAC','strokeColor':'#265A70','enableAnimation':False,'enableFlashing':False}
fixture={'schemaVersion':1,'name':'Symbol Editor Lab','views':[{'path':'Main','width':1000,'height':640,'root':root}],
    'pages':[{'path':'/','view':'Main'}],'symbolStates':{'states':{'service':{'light':style,'dark':style,'symbols':['Pump']}}},'tags':[],'alarms':[]}
fixture_file=OUT/'fixture.json';fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1700,'height':1100},accept_downloads=True);page=context.new_page()
    page.on('pageerror',lambda e:logs.append({'kind':'pageerror','text':str(e)}));page.on('console',lambda e:logs.append({'kind':e.type,'text':e.text}))
    def app():return page.evaluate('window.scadaApp')
    def component(name):return page.evaluate('n=>window.scadaViews[0].components[n]',name)
    def symbol(name):return component(name)['symbol']
    def wait(js):page.wait_for_function(js,timeout=30000)
    def click(b):
        assert b and b['width']>0 and b['height']>0,'Expected arranged native control'
        page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
    def command(key):click(wait_command_bounds(page,key))
    def select(name):
        page.wait_for_function('n=>window.scadaViews?.[0]?.components[n]?.bounds?.width>0 && window.scadaViews[0].components[n].bounds.height>0',arg=name,timeout=30000)
        click(component(name)['bounds'])
        page.wait_for_function('n=>window.scadaApp.selection[0]===n && window.scadaApp.inspectorChoices && window.scadaApp.inspector.name?.width>0',arg=name,timeout=30000)
    def menu(key):
        page.wait_for_function('k=>window.scadaApp.inspector[k]?.width>0 && window.scadaApp.inspectorChoices[k]?.enabled',arg=key,timeout=30000)
        click(app()['inspector'][key]);page.wait_for_function('k=>window.scadaApp.inspectorChoices[k]?.options?.some(o=>o.bounds?.width>0)',arg=key,timeout=30000)
        return app()['inspectorChoices'][key]['options']
    def choose(key,value):
        before=app()['generation'];rows=menu(key)
        target=next(o for o in rows if o['name']==value)
        assert target['bounds'],'Choice is not visible'
        click(target['bounds']);wait('window.scadaApp.generation>'+str(before))
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
        event.value.set_files(fixture_file);wait("window.scadaApp.project==='Symbol Editor Lab'");command('fit')
        wait('window.scadaViews?.[0]?.components.Pump?.symbol?.renders>0')
        select('Pump');check('pump properties expose real family-specific dropdowns',all(k in app()['inspectorChoices'] for k in ['appearance','orientation','variant','feet','state']))
        for name in ['Pump','Motor','Valve','Sensor','Vessel']:
            select(name);choose('appearance','p&id')
            page.wait_for_function('n=>window.scadaViews[0].components[n].symbol.options.appearance==="p&id" && window.scadaViews[0].components[n].symbol.renderError===null',arg=name,timeout=30000)
            check(name+' appearance choice drives its actual schematic renderer',symbol(name)['options']['appearance']=='p&id')
        select('Pump');choose('appearance','simple');choose('variant','vacuum')
        wait("window.scadaViews[0].components.Pump.symbol.options.variant==='vacuum'")
        check('pump variant dropdown switches to the actual vacuum drawing',symbol('Pump')['options']['variant']=='vacuum')
        choose('orientation','top');wait("window.scadaViews[0].components.Pump.symbol.options.orientation==='top'")
        check('equipment direction is independently configurable',symbol('Pump')['options']['variant']=='vacuum')
        choose('variant','centrifugal');choose('feet','left');wait("window.scadaViews[0].components.Pump.symbol.options.feet==='left'")
        check('pump feet keep an independent direction choice',symbol('Pump')['options']['orientation']=='top')
        rows=menu('state');available=[r['name'] for r in rows];page.keyboard.press('Escape');wait('window.scadaApp.inspectorChoices.state.options===null')
        check('custom state choices are filtered to their applied family','service' in available and 'open' not in available)
        choose('state','service');wait("window.scadaViews[0].components.Pump.symbol.options.state==='service'")
        check('custom state selection uses its project-defined graphics materials',symbol('Pump')['options']['stateStyle']['primaryColor']=='#67A9CC')
        select('Valve');rows=menu('state');available=[r['name'] for r in rows];page.keyboard.press('Escape');wait('window.scadaApp.inspectorChoices.state.options===null')
        check('valve choices keep all six built-in states without unrelated machine states',len(available)==6 and 'partiallyClosed' in available and 'service' not in available and 'running' not in available)
        choose('state','partiallyClosed');wait("window.scadaViews[0].components.Valve.symbol.options.state==='partiallyClosed'")
        select('Vessel');choose('orientation','horizontal');wait("window.scadaViews[0].components.Vessel.symbol.options.orientation==='horizontal'")
        check('vessel directions use horizontal and vertical rather than machine directions',symbol('Vessel')['options']['orientation']=='horizontal')
        command('undo');wait("window.scadaViews[0].components.Vessel.symbol.options.orientation==='vertical'")
        check('symbol property choices participate in ordinary undo and redo',app()['canRedo'])
        command('redo');wait("window.scadaViews[0].components.Vessel.symbol.options.orientation==='horizontal'")
        select('BoundPump');wait("window.scadaApp.inspectorChoices.state?.selected==='default'")
        check('qualified bound state properties remain read-only native choices',not app()['inspectorChoices']['state']['enabled'])
        select('InvalidMotor');wait("window.scadaApp.inspectorChoices.state?.selected==='missing'")
        check('unknown saved states stay selected and visibly diagnosed',symbol('InvalidMotor')['dataError'] is not None and app()['inspectorChoices']['state']['selected']=='missing')
        choose('state','running');wait("window.scadaViews[0].components.InvalidMotor.symbol.dataError===null && window.scadaViews[0].components.InvalidMotor.symbol.renderError===null")
        check('repairing an invalid state uses the standard native editing transaction',symbol('InvalidMotor')['options']['state']=='running')
        select('Pipe');choose('route','straight');wait("window.scadaApp.inspectorChoices.route?.selected==='straight'")
        check('pipe routing uses the supported native preset choices',not symbol('Pipe')['renderError'])
        select('Pump');page.screenshot(path=str(OUT/'symbol-editor.png'),full_page=True)
        baseline=export('configured.json');command('preview');wait('window.scadaApp.preview')
        check('runtime preview retains edited graphics without modifying project settings',export('runtime.json')==baseline)
        command('preview');wait('!window.scadaApp.preview')
        page.set_viewport_size({'width':1024,'height':768});wait('window.scadaApp.viewport.width===1024');command('fit');select('Pump')
        choose('appearance','mimic');wait("window.scadaViews[0].components.Pump.symbol.options.appearance==='mimic'")
        check('native symbol choices stay usable in the tablet layout',symbol('Pump')['dataError'] is None and symbol('Pump')['renderError'] is None)
        page.screenshot(path=str(OUT/'symbol-editor-tablet.png'),full_page=True)
        check('all symbol editor workflows finish without unhandled browser or application errors',not app()['errors'] and not app()['diagnostics'] and not any(e['kind']=='pageerror' for e in logs))
    except Exception as error:
        results.append({'name':'symbol editor suite','passed':False,'error':str(error)});page.screenshot(path=str(OUT/'failure.png'),full_page=True);raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'),indent=2));browser.close()
