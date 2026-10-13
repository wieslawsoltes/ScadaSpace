"""Actual Uno LED rendering, flat native inspector choices, bindings and export isolation."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright
from native_readiness import wait_command_bounds

OUT=Path('TestResults/browser-led');OUT.mkdir(parents=True,exist_ok=True)
BASE=os.environ.get('SCADASPACE_URL','http://127.0.0.1:8765/ScadaSpace/')
results,logs=[],[]
def scalar(v):
    if isinstance(v,bool):return {'kind':'Boolean','boolean':v}
    if isinstance(v,(float,int)):return {'kind':'Number','number':v}
    return {'kind':'String','text':v}
def node(name,kind,x,y,w,h,**props):return {'id':name,'name':name,'type':kind,'x':x,'y':y,'width':w,'height':h,'props':{k:scalar(v) for k,v in props.items()}}
def binding(key,write=False):return {'kind':'Property','source':'root.custom.'+key,'bidirectional':write}
root=node('Root','Container',0,0,1000,650)
root['custom']={k:scalar(v) for k,v in {'number':12345.67,'locale':'en-US','format':'#,##0.00','text':'RUN P-101','segment':'14 segment'}.items()}
children=[node('Heading','Label',20,8,960,40,text='LED DISPLAYS · segments, formats and isolated runtime values',fontSize=22)]
for name,key,x,width in [('LocaleInput','locale',200,150),('FormatInput','format',370,150),('TextInput','text',540,420),('SegmentInput','segment',20,190)]:
    n=node(name,'TextField',x,100 if name=='SegmentInput' else 55,width,34,text=root['custom'][key]['text']);n['bindings']={'text':binding(key,True)};children.append(n)
n=node('ValueInput','NumericEntry',20,55,160,34,value=12345.67,minimum=-1000000,maximum=1000000);n['bindings']={'value':binding('number',True)};children.append(n)
for name,x,segment in [('Seven',20,'7 segment'),('Fourteen',510,'14 segment')]:
    n=node(name,'LEDDisplay',x,165,450,100,value=12345.67,segmentFormat=segment,numberFormat='#,##0.00',locale='en-US',backgroundColor='#161616',diodeOnColor='#1EC963',diodeOffColor='#193B29')
    n['bindings']={'value':binding('number'),'locale':binding('locale'),'props.numberFormat':binding('format')};children.append(n)
n=node('TextLed','LEDDisplay',20,305,940,95,value='RUN P-101',segmentFormat='14 segment',numberFormat='0',backgroundColor='#102A38',diodeOnColor='#55D4EA',diodeOffColor='#214352')
n['bindings']={'value':binding('text'),'segmentFormat':binding('segment')};children.append(n)
children.extend([node('Legacy','LEDDisplay',20,455,260,90,value=42.6,format='0.0'),
                 node('ColorLed','LEDDisplay',330,455,310,90,value=12.34,segmentFormat='7 segment',numberFormat='0.00',backgroundColor='#D5D5D5',diodeOnColor='#0062FF',diodeOffColor='#1A0062FF'),
                 node('Tall','LEDDisplay',720,440,240,180,value=-123.45,segmentFormat='14 segment',numberFormat='0.00',unit='m³/h')])
root['children']=children
fixture={'schemaVersion':1,'name':'LED Display Lab','views':[{'path':'Main','width':1000,'height':650,'root':root}],'pages':[{'path':'/','view':'Main'}],'tags':[],'alarms':[]}
fixture_file=OUT/'fixture.json';fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1700,'height':1100},accept_downloads=True);page=context.new_page()
    page.on('pageerror',lambda e:logs.append({'kind':'pageerror','text':str(e)}));page.on('console',lambda e:logs.append({'kind':e.type,'text':e.text}))
    def app():return page.evaluate('window.scadaApp')
    def views():return page.evaluate('window.scadaViews')
    def component(name):return views()[0]['components'][name]
    def symbol(name):return component(name)['symbol']
    def wait(js):page.wait_for_function(js,timeout=30000)
    def tick(n=2):
        before=app()['tick'];wait('window.scadaApp.tick>'+str(before+n))
    def click(box):
        assert box and box['width']>0 and box['height']>0,'Native target must be arranged'
        page.mouse.click(box['x']+box['width']/2,box['y']+box['height']/2)
    def command(key):click(wait_command_bounds(page,key))
    def fill(name,text):
        click(component(name)['bounds']);page.keyboard.press('Control+a');page.keyboard.press('Backspace');page.keyboard.insert_text(str(text));page.keyboard.press('Enter')
    def choose(key,text):
        # Selection publishes before the rebuilt native fields finish layout.
        # Wait for the selected component's actual editable ComboBox, not its model alone.
        selected=app()['selection']
        page.wait_for_function('p=>JSON.stringify(window.scadaApp.selection)===JSON.stringify(p.selected) && window.scadaApp.inspector[p.key]?.width>0 && window.scadaApp.inspector[p.key]?.height>0 && window.scadaApp.inspectorChoices[p.key]?.enabled===true',arg={'key':key,'selected':selected},timeout=30000)
        click(app()['inspector'][key])
        page.wait_for_function('p=>window.scadaApp.inspectorChoices[p.key]?.options?.some(o=>o.name===p.text && o.bounds?.width>0)',arg={'key':key,'text':text},timeout=30000)
        click(next(o['bounds'] for o in app()['inspectorChoices'][key]['options'] if o['name']==text))
    def export(name):
        with page.expect_download() as event:command('export')
        target=OUT/name;event.value.save_as(target);return json.loads(target.read_text())
    def check(name,ok):
        results.append({'name':name,'passed':bool(ok)})
        if not ok:raise AssertionError(name)
        print('PASS',name,flush=True)
    names=['Seven','Fourteen','TextLed','Legacy','ColorLed','Tall']
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000);page.wait_for_function('window.scadaApp?.ready',timeout=120000)
        with page.expect_file_chooser() as chooser:command('import')
        chooser.value.set_files(fixture_file);wait("window.scadaApp.project==='LED Display Lab'");command('fit')
        page.wait_for_function('names=>names.every(n=>window.scadaViews?.[0]?.components[n]?.symbol?.renders>0)',arg=names,timeout=30000)
        check('all LED variants and alphanumeric readouts render through actual Uno controls',all(not symbol(n)['dataError'] and not symbol(n)['renderError'] for n in names))
        check('legacy display retains its existing colors format and seven-segment mode',symbol('Legacy')['led']['text']=='42.6' and symbol('Legacy')['led']['options']['backgroundColor']=='#22332D' and symbol('Legacy')['led']['options']['segmentFormat']=='7 segment')
        click(component('Seven')['bounds']);wait("window.scadaApp.selection[0]==='Seven' && window.scadaApp.inspectorChoices.segmentFormat")
        check('Property Editor exposes segment choices and protects qualified bound format fields',app()['inspectorChoices']['segmentFormat']['enabled'] and not app()['inspectorChoices']['numberFormat']['enabled'])
        generation=app()['generation'];choose('segmentFormat','14 segment');wait('window.scadaApp.generation>'+str(generation))
        wait("window.scadaViews[0].components.Seven.symbol.led.options.segmentFormat==='14 segment'")
        check('native segment selection creates an undoable designer transaction',app()['canUndo'] and app()['generation']==generation+1)
        command('undo');wait("window.scadaViews[0].components.Seven.symbol.led.options.segmentFormat==='7 segment'")
        check('undo restores the original LED mode without dirtying the project',not app()['dirty'])
        click(component('ColorLed')['bounds']);wait("window.scadaApp.selection[0]==='ColorLed'")
        choose('numberFormat','0.0');wait("window.scadaViews[0].components.ColorLed.symbol.led.text==='12.3'")
        check('native number-format selector changes the displayed precision',symbol('ColorLed')['led']['options']['numberFormat']=='0.0')
        command('undo');wait("window.scadaViews[0].components.ColorLed.symbol.led.text==='12.34'")
        page.screenshot(path=str(OUT/'led-designer.png'),full_page=True)
        baseline=export('baseline.json');old=views()[0]['id'];command('preview')
        page.wait_for_function('args=>window.scadaViews?.[0]?.id!==args.old && window.scadaViews?.[0]?.editing===false && args.names.every(n=>window.scadaViews[0].components[n].symbol.renders>0)',arg={'old':old,'names':names},timeout=30000)
        before={n:(symbol(n)['id'],symbol(n)['pictures']) for n in names};tick(5)
        check('unchanged LED bindings reuse the same controls and cached pictures',all((symbol(n)['id'],symbol(n)['pictures'])==before[n] for n in names))
        check('numeric and text presentations remain distinct in the runtime',symbol('Fourteen')['led']['text']=='12,345.67' and symbol('TextLed')['led']['text']=='RUN P-101')
        fill('LocaleInput','de-DE');wait("window.scadaViews[0].components.Fourteen.symbol.led.text==='12.345,67'")
        check('locale binding changes grouping and decimal punctuation in both segment modes',symbol('Seven')['led']['text']=='12.345,67')
        fill('LocaleInput','pl-PL');wait("window.scadaViews[0].components.Fourteen.symbol.led.options.locale==='pl-PL'")
        check('Polish locale renders its grouping space and decimal comma',symbol('Fourteen')['led']['text'].endswith(',67') and not symbol('Fourteen')['renderError'])
        fill('LocaleInput','en-US');fill('ValueInput',.125);fill('FormatInput','0.0%');wait("window.scadaViews[0].components.Fourteen.symbol.led.text==='12.5%'")
        check('percentage formatting preserves the source numeric value',symbol('Fourteen')['value']==.125 and symbol('Seven')['led']['text']=='12.5%')
        fill('TextInput','ALARM 12:34');wait("window.scadaViews[0].components.TextLed.symbol.led.text==='ALARM 12:34'")
        check('fourteen-segment runtime text includes letters and colon punctuation',not symbol('TextLed')['dataError'])
        page.screenshot(path=str(OUT/'led-runtime.png'),full_page=True)
        identity=symbol('TextLed')['id'];fill('SegmentInput','7 segment');wait('window.scadaViews[0].components.TextLed.symbol.dataError!==null')
        check('unrepresentable seven-segment letters report a diagnostic rather than blank data','not supported' in symbol('TextLed')['dataError'] and symbol('Fourteen')['dataError'] is None)
        renders=symbol('TextLed')['renders'];fill('SegmentInput','14 segment')
        wait('window.scadaViews[0].components.TextLed.symbol.dataError===null && window.scadaViews[0].components.TextLed.symbol.renderError===null && window.scadaViews[0].components.TextLed.symbol.renders>'+str(renders))
        check('correcting the segment mode recovers the same native control',symbol('TextLed')['id']==identity)
        fill('TextInput','1'*65);wait('window.scadaViews[0].components.TextLed.symbol.dataError!==null')
        check('oversized display text is explicitly bounded','64 characters' in symbol('TextLed')['dataError'])
        fill('TextInput','RUN P-101');wait('window.scadaViews[0].components.TextLed.symbol.dataError===null')
        fill('FormatInput','F100000');wait('window.scadaViews[0].components.Fourteen.symbol.dataError!==null')
        check('excessive numeric precision is rejected before building huge geometry','precision' in symbol('Fourteen')['dataError'])
        fill('FormatInput','#,##0.00');fill('ValueInput',-12345.67)
        wait("window.scadaViews[0].components.Fourteen.symbol.led.text==='-12,345.67' && window.scadaViews[0].components.Fourteen.symbol.renderError===null")
        check('numeric formatting errors recover through normal bound input',not symbol('Seven')['dataError'])
        check('runtime presentation changes do not mutate complete exported project defaults',export('after-runtime.json')==baseline and not app()['dirty'])
        page.set_viewport_size({'width':1024,'height':768});tick(3);command('fit');tick(3)
        check('LED layouts remain valid at tablet dimensions without changing values',all(not symbol(n)['renderError'] and not symbol(n)['dataError'] for n in names) and symbol('Fourteen')['value']==-12345.67)
        page.screenshot(path=str(OUT/'led-tablet.png'),full_page=True)
        check('LED interactions finish without unhandled browser or managed errors',not app()['errors'] and not app()['diagnostics'] and not any(e['kind']=='pageerror' for e in logs))
    except Exception as error:
        results.append({'name':'LED browser suite','passed':False,'error':str(error)});page.screenshot(path=str(OUT/'failure.png'),full_page=True);raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'),indent=2));browser.close()
