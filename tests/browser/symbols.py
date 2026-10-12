"""Actual Uno industrial symbols: bound native inputs, retained vector pictures, animation and gallery defaults."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path('TestResults/browser-symbols'); OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get('SCADASPACE_URL', 'http://127.0.0.1:8765/ScadaSpace/')
results, logs = [], []
def scalar(v):
    if v is None: return {'kind':'Null'}
    if isinstance(v,bool): return {'kind':'Boolean','boolean':v}
    if isinstance(v,(int,float)): return {'kind':'Number','number':v}
    if isinstance(v,str): return {'kind':'String','text':v}
    if isinstance(v,list): return {'kind':'Array','items':[scalar(x) for x in v]}
    return {'kind':'Object','fields':{k:scalar(x) for k,x in v.items()}}
def node(name,kind,x,y,w,h,props=None,bindings=None):
    return {'id':name,'name':name,'type':kind,'x':x,'y':y,'width':w,'height':h,'props':{k:scalar(v) for k,v in (props or {}).items()},'bindings':bindings or {}}
def bind(source,kind='Property',write=False): return {'kind':kind,'source':source,'bidirectional':write}
root=node('Root','Container',0,0,1100,760)
root['custom']={k:scalar(v) for k,v in {'appearance':'auto','state':'stopped','orientation':'right','speed':50,'level':45,'broken':False,'vesselOrientation':'vertical','agitator':True,'fill':True}.items()}
children=[node('Title','Label',20,10,1000,40,{'text':'INDUSTRIAL SYMBOLS · retained vectors and native state','fontSize':23})]
for name,key,x,width in [('Appearance','appearance',20,160),('State','state',200,160),('Orientation','orientation',380,150),('VesselOrientation','vesselOrientation',550,170)]:
    children.append(node(name,'TextField',x,72,width,34,{'text':''},{'text':bind('root.custom.'+key,write=True)}))
children += [node('Speed','NumericEntry',740,72,145,34,{'value':50,'minimum':0,'maximum':100},{'value':bind('root.custom.speed',write=True)}),
             node('Level','NumericEntry',905,72,145,34,{'value':45,'minimum':0,'maximum':200},{'value':bind('root.custom.level',write=True)}),
             node('Broken','CheckBox',20,121,210,32,{'text':'Invalid pump binding','value':False},{'value':bind('root.custom.broken',write=True)}),
             node('Agitator','CheckBox',260,121,170,32,{'text':'Agitator','value':True},{'value':bind('root.custom.agitator',write=True)}),
             node('Fill','CheckBox',460,121,160,32,{'text':'Fill level','value':True},{'value':bind('root.custom.fill',write=True)})]
for i,(name,kind) in enumerate([('Pump','Pump'),('Vacuum','Pump'),('Motor','Motor'),('Valve','Valve'),('Sensor','Sensor'),('Vessel','Vessel')]):
    props={'value':0,'appearance':'auto','state':'stopped','orientation':'vertical' if kind=='Vessel' else 'right','animationSpeed':'auto','label':{'text':name,'location':'bottom'}}
    bindings={'appearance':bind('root.custom.appearance'),'animationSpeed':bind('root.custom.speed'),'state':bind('root.custom.state'),
              'orientation':bind('root.custom.vesselOrientation' if kind=='Vessel' else 'root.custom.orientation')}
    if name=='Pump': bindings['state']=bind("if({root.custom.broken}, 1/0, {root.custom.state})",'Expression')
    if name=='Vacuum': props['variant']='vacuum'
    if kind=='Valve': bindings['state']=bind("if({root.custom.state} == 'running', 'open', if({root.custom.state} == 'faulted', 'failedToOpen', 'closed'))",'Expression')
    if kind=='Vessel':
        props['value']={'value':45,'capacity':200,'displayValueAsPercent':True,'location':'top'}
        bindings['value.value']=bind('root.custom.level');bindings['displayAgitator']=bind('root.custom.agitator');bindings['displayFillLevel']=bind('root.custom.fill')
    children.append(node(name,kind,20+i*180,181,160,265,props,bindings))
children += [node('Tank','Tank',30,495,180,235,{'value':45,'capacity':200,'warningThreshold':25,'liquidOpacity':.55,'valueDisplay':{'format':'Percent','unit':{'enabled':False}}},{'value':bind('root.custom.level')}),
             node('Gauge','Gauge',250,505,220,160,{'value':45,'minimum':0,'maximum':200},{'value':bind('root.custom.level')}),
             node('Thermometer','Thermometer',515,495,105,235,{'value':45,'minimum':0,'maximum':200},{'value':bind('root.custom.level')}),
             node('LED','LEDDisplay',680,525,180,80,{'value':45,'format':'0.00','unit':'m³'},{'value':bind('root.custom.level')}),
             node('Pipe','Pipe',900,535,150,80,{'value':True,'strokeWidth':12,'color':'#00ACAC'})]
root['children']=children
fixture={'schemaVersion':1,'name':'Industrial Symbol Lab','views':[{'path':'Symbols','width':1100,'height':760,'root':root}], 'pages':[{'path':'/','view':'Symbols'}], 'tags':[], 'alarms':[]}
fixture_file=OUT/'fixture.json';fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1700,'height':1100},accept_downloads=True)
    page=context.new_page()
    page.on('pageerror',lambda e:logs.append({'kind':'pageerror','text':str(e)}))
    page.on('console',lambda e:logs.append({'kind':e.type,'text':e.text}))
    def app():return page.evaluate('window.scadaApp')
    def views():return page.evaluate('window.scadaViews')
    def component(name):return views()[0]['components'][name]
    def symbol(name):return component(name)['symbol']
    def wait(js):page.wait_for_function(js,timeout=30000)
    def click(box):
        assert box and box['width']>0 and box['height']>0,'Missing rendered native bounds'
        page.mouse.click(box['x']+box['width']/2,box['y']+box['height']/2)
    def command(key):
        page.wait_for_function('k=>window.scadaApp.controls[k]?.width>0',arg=key,timeout=30000)
        click(app()['controls'][key]);page.wait_for_timeout(300)
    def fill(name,text):
        click(component(name)['bounds']);page.keyboard.press('Control+a');page.keyboard.press('Backspace');page.keyboard.insert_text(str(text));page.keyboard.press('Enter');page.wait_for_timeout(350)
    def ticks(count=3):
        tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+count))
    def check(name,ok):
        results.append({'name':name,'passed':bool(ok)})
        if not ok:raise AssertionError(name)
        print('PASS',name,flush=True)
    names=['Pump','Vacuum','Motor','Valve','Sensor','Vessel','Tank','Gauge','Thermometer','LED','Pipe']
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000)
        page.wait_for_function('window.scadaApp?.ready && window.scadaApp.tick>2',timeout=120000)
        with page.expect_file_chooser() as chosen:command('import')
        chosen.value.set_files(fixture_file);wait("window.scadaApp.project==='Industrial Symbol Lab'");command('fit')
        wait("window.scadaViews?.[0]?.components?.Vessel?.symbol?.pictures>0")
        check('all industrial and existing display symbols render through native Uno',all(symbol(n)['pictures']>0 and symbol(n)['renderError'] is None and symbol(n)['dataError'] is None for n in names))
        check('new Sensor and Vessel are real catalog controls',symbol('Sensor')['kind']=='Sensor' and symbol('Vessel')['kind']=='Vessel' and app()['workspace']['paletteButtonsCreated']==35)
        check('design mode never schedules symbol motion',all(not symbol(n)['animating'] and symbol(n)['animationFrames']==0 for n in names))
        command('preview');wait('window.scadaApp.preview');ticks()
        before={n:symbol(n)['pictures'] for n in names};ticks(5)
        check('idle runtime refreshes reuse every unchanged vector picture',all(symbol(n)['pictures']==before[n] for n in names))
        check('vessel readout uses its independent capacity',symbol('Vessel')['maximum']==200 and symbol('Vessel')['options']['readout']['text']=='22.5%')
        page.screenshot(path=str(OUT/'symbols-mimic.png'),full_page=True)
        for appearance in ['simple','p&id','mimic']:
            fill('Appearance',appearance)
            wait("window.scadaViews[0].components.Pump.symbol.options.appearance==="+json.dumps(appearance))
            check('bound '+appearance+' appearance reaches all industrial renderers',all(symbol(n)['options']['appearance']==appearance and symbol(n)['renderError'] is None for n in names[:6]))
            page.screenshot(path=str(OUT/('symbols-'+appearance.replace('&','n')+'.png')),full_page=True)
        fill('State','running');wait('window.scadaViews[0].components.Pump.symbol.animating');ticks(2)
        frames=symbol('Pump')['animationFrames'];pictures=symbol('Pump')['pictures'];ticks(4)
        check('running pump animation advances without rebuilding static geometry',symbol('Pump')['animationFrames']>frames and symbol('Pump')['pictures']==pictures)
        check('valve machine mapping and pump variant remain distinct',symbol('Valve')['options']['state']=='open' and symbol('Vacuum')['options']['variant']=='vacuum')
        fill('Speed',0);wait('!window.scadaViews[0].components.Pump.symbol.animating');frames=symbol('Pump')['animationFrames'];ticks(4)
        check('zero animation speed stops redraw scheduling',symbol('Pump')['animationFrames']==frames)
        fill('Speed',80);wait('window.scadaViews[0].components.Pump.symbol.animating')
        fill('State','faulted');wait("window.scadaViews[0].components.Pump.symbol.options.state==='faulted'")
        check('faulted state stops motion and preserves a distinct valve failure',not symbol('Pump')['animating'] and symbol('Valve')['options']['state']=='failedToOpen')
        fill('State','running');fill('Orientation','top');wait("window.scadaViews[0].components.Pump.symbol.options.orientation==='top'")
        check('orientation is bound independently from machine state',symbol('Motor')['options']['orientation']=='top' and symbol('Pump')['options']['state']=='running')
        fill('VesselOrientation','horizontal');fill('Level',125)
        wait("window.scadaViews[0].components.Vessel.symbol.options.readout.text==='62.5%'")
        check('horizontal vessels retain typed fill and readout data',symbol('Vessel')['options']['orientation']=='horizontal' and symbol('Vessel')['value']==125 and symbol('Tank')['value']==125)
        click(component('Agitator')['bounds']);click(component('Fill')['bounds']);ticks()
        check('vessel stand agitator and fill settings remain independently applied',not symbol('Vessel')['options']['displayAgitator'] and not symbol('Vessel')['options']['displayFillLevel'] and symbol('Vessel')['options']['displayStand'])
        click(component('Broken')['bounds']);wait('window.scadaViews[0].components.Pump.symbol.dataError!==null')
        check('failed symbol bindings show an explicit error instead of stale good animation',not symbol('Pump')['animating'] and 'BadExpression' in symbol('Pump')['dataError'] and symbol('Motor')['dataError'] is None)
        frames=symbol('Pump')['animationFrames'];ticks(4)
        check('failed data stops the affected symbol clock',symbol('Pump')['animationFrames']==frames)
        click(component('Broken')['bounds']);wait('window.scadaViews[0].components.Pump.symbol.dataError===null && window.scadaViews[0].components.Pump.symbol.animating')
        check('corrected bindings recover the same native symbol',symbol('Pump')['renderError'] is None and not views()[0]['diagnostics'])
        fill('Appearance','invalid');wait('window.scadaViews[0].components.Pump.symbol.dataError!==null')
        check('unknown appearances are diagnosed rather than silently substituted',symbol('Pump')['dataError'] is not None and symbol('Sensor')['dataError'] is not None)
        fill('Appearance','auto');fill('Orientation','right');fill('State','stopped');ticks()
        command('menu-Tools');command('symbol-library');wait('window.scadaApp.symbolLibrary!==null')
        check('native Symbol Library opens all preview families',len(app()['symbolLibrary']['symbols'])==9)
        click(app()['symbolLibrary']['fields']['appearance'])
        wait("window.scadaApp.symbolLibrary.menus.appearance?.some(i=>i.name==='p&id' && i.bounds?.width>0)")
        click(next(i['bounds'] for i in app()['symbolLibrary']['menus']['appearance'] if i['name']=='p&id'))
        wait("window.scadaApp.symbolLibrary.defaults.autoAppearance==='p&id'")
        click(app()['symbolLibrary']['fields']['reduced']);ticks()
        check('library appearance and reduced motion update retained native previews',app()['symbolLibrary']['defaults']['reducedMotion'] and all(not s['animating'] for s in app()['symbolLibrary']['symbols'].values()))
        page.screenshot(path=str(OUT/'symbol-library.png'),full_page=True)
        command('configuration-dialog-ok');wait('window.scadaApp.symbolLibrary===null');ticks()
        check('automatic symbol appearance follows local session defaults',symbol('Pump')['options']['appearance']=='p&id' and app()['symbolDefaults']['reducedMotion'])
        fill('State','running');ticks()
        check('reduced motion preserves running state without animation',symbol('Pump')['options']['state']=='running' and not symbol('Pump')['animating'])
        with page.expect_download() as event:command('export')
        event.value.save_as(OUT/'runtime-export.json');exported=json.loads((OUT/'runtime-export.json').read_text())
        check('runtime symbol preferences and inputs never mutate exported defaults',exported['views'][0]['root']['custom']==fixture['views'][0]['root']['custom'] and not app()['dirty'])
        page.set_viewport_size({'width':1024,'height':768});ticks(4);command('fit');ticks(3)
        check('all symbol renderers remain valid at tablet dimensions',all(symbol(n)['renderError'] is None and symbol(n)['dataError'] is None for n in names))
        page.screenshot(path=str(OUT/'symbols-tablet.png'),full_page=True)
        check('symbol interactions complete without unmanaged browser or managed application errors',not app()['errors'] and not app()['diagnostics'] and not any(e['kind']=='pageerror' for e in logs))
    except Exception as error:
        results.append({'name':'symbol browser run','passed':False,'error':str(error)});page.screenshot(path=str(OUT/'failure.png'),full_page=True);raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'),indent=2));browser.close()
