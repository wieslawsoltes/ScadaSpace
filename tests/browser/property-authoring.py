"""Actual native typed creation, lossless field editing and retained binding text."""
from __future__ import annotations
import json
import os
import time
from pathlib import Path
from playwright.sync_api import sync_playwright
from native_readiness import CommandReadiness, wait_command_bounds, wait_component_sample

OUT=Path('TestResults/browser-property-authoring');OUT.mkdir(parents=True,exist_ok=True)
BASE=os.environ.get('SCADASPACE_URL','http://127.0.0.1:8765/ScadaSpace/')
results,logs=[],[]
def scalar(v):
    if v is None:return {'kind':'Null'}
    if isinstance(v,bool):return {'kind':'Boolean','boolean':v}
    if isinstance(v,(float,int)):return {'kind':'Number','number':v}
    if isinstance(v,dict):return {'kind':'Object','fields':{k:scalar(x) for k,x in v.items()}}
    if isinstance(v,list):return {'kind':'Array','items':[scalar(x) for x in v]}
    return {'kind':'String','text':v}
def node(name,kind,x,y,w,h,**props):
    return {'id':name,'name':name,'type':kind,'x':x,'y':y,'width':w,'height':h,'props':{k:scalar(v) for k,v in props.items()}}
root=node('Root','Container',0,0,1000,640);root['custom']={'sample':scalar(1.23456789012345)}
probe=node('Probe','LEDDisplay',50,110,410,130,value=1.23456789012345,segmentFormat='14 segment',numberFormat='0.0',backgroundColor='#161616',diodeOnColor='#1EC963',diodeOffColor='#193B29')
probe['bindings']={'props.value':{'kind':'Property','source':'root.custom.sample'}}
probe['custom']={k:scalar(v) for k,v in {'exact':1.23456789012345,'tiny':1.23456789e-7,'flag':True,'text':'operator note','settings':{'target':1.23456789,'values':[True,None]},'empty':None}.items()}
root['children']=[node('Title','Label',20,10,950,40,text='PROPERTY AUTHORING · typed values and exact numbers',fontSize=23),probe,
    node('Vessel','Vessel',550,100,220,300,value=45,state='default',appearance='mimic',orientation='vertical',liquidColor='#00ACAC')]
fixture={'schemaVersion':1,'name':'Typed Property Lab','views':[{'path':'Main','width':1000,'height':640,'root':root}],'pages':[{'path':'/','view':'Main'}],'tags':[],'alarms':[]}
fixture_file=OUT/'fixture.json';fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1700,'height':1100},accept_downloads=True);page=context.new_page()
    page.on('pageerror',lambda e:logs.append({'kind':'pageerror','text':str(e)}));page.on('console',lambda e:logs.append({'kind':e.type,'text':e.text}))
    def app():return page.evaluate('window.scadaApp')
    def author():return app()['inspectorAuthoring']
    def creation():return author()['creation']
    def wait(js):page.wait_for_function(js,timeout=30000)
    def click(b):
        assert b and b['width']>0 and b['height']>0,'Expected arranged native bounds'
        page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
    def bounds(path):
        tracker=CommandReadiness();deadline=time.monotonic()+30
        while time.monotonic()<deadline:
            sample=page.evaluate('''path=>{let b=window.scadaApp;for(const k of path)b=b?.[k];return {tick:window.scadaApp.tick,project:window.scadaApp.project,
                viewId:window.scadaViews?.[0]?.id,viewport:window.scadaApp.viewport,bounds:b};}''',path)
            if tracker.accept(sample):return sample['bounds']
            page.wait_for_function('t=>window.scadaApp.tick>t',arg=sample['tick'],timeout=30000)
        raise TimeoutError('Native geometry did not settle: '+str(path))
    def command(key):click(wait_command_bounds(page,key))
    def search(text):
        click(bounds(['inspectorFilter','controls','search']));page.keyboard.press('Control+a');page.keyboard.press('Backspace')
        if text:page.keyboard.insert_text(text)
        page.wait_for_function('q=>window.scadaApp.inspectorFilter.query===q',arg=text,timeout=30000)
    def select(name):
        sample=wait_component_sample(page,name);logs.append({'kind':'component-input','name':name,**sample});click(sample['bounds'])
        page.wait_for_function('n=>window.scadaApp.selection[0]===n && window.scadaApp.inspectorFilter.total>0',arg=name,timeout=30000)
    def input_text(path,text,enter=False):
        click(bounds(path));page.keyboard.press('Control+a');page.keyboard.press('Backspace')
        if text:page.keyboard.insert_text(text)
        if enter:page.keyboard.press('Enter')
    def edit(path,text):input_text(['inspectorQualified',path],text,True)
    def open_creation(group='custom'):
        search(group+'.');click(bounds(['inspectorAuthoring','add',group]));wait('window.scadaApp.inspectorAuthoring.creation!==null')
    def field(key,text):input_text(['inspectorAuthoring','creation','fields',key],text)
    def choose(kind):
        click(bounds(['inspectorAuthoring','creation','fields','kind']))
        page.wait_for_function('k=>window.scadaApp.inspectorAuthoring.creation.options?.some(v=>v.name===k&&v.bounds?.width>0)',arg=kind,timeout=30000)
        click(next(o['bounds'] for o in creation()['options'] if o['name']==kind));page.wait_for_function('k=>window.scadaApp.inspectorAuthoring.creation.kind===k && window.scadaApp.inspectorAuthoring.creation.options===null',arg=kind,timeout=30000)
    def submit():click(bounds(['inspectorAuthoring','creation','commands','add']))
    def cancel():click(bounds(['inspectorAuthoring','creation','commands','cancel']));wait('window.scadaApp.inspectorAuthoring.creation===null')
    def export(name):
        with page.expect_download() as event:command('export')
        file=OUT/name;event.value.save_as(file);return json.loads(file.read_text())
    def saved_probe(project):return next(n for n in project['views'][0]['root']['children'] if n['name']=='Probe')
    def check(name,value):
        results.append({'name':name,'passed':bool(value)})
        if not value:raise AssertionError(name)
        print('PASS',name,flush=True)
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000);page.wait_for_function('window.scadaApp?.ready',timeout=120000)
        with page.expect_file_chooser() as event:command('import')
        event.value.set_files(fixture_file);wait("window.scadaApp.project==='Typed Property Lab'");command('fit');select('Probe')
        initial=export('initial.json');search('custom.')
        check('precise and tiny property numbers are not rounded to process display precision',author()['fields']['custom.exact']['text']=='1.23456789012345' and float(author()['fields']['custom.tiny']['text'])==1.23456789e-7)
        check('number Boolean string and null values use native type-specific foregrounds',all(author()['fields'][key]['foreground']==color for key,color in [('custom.exact','#FFA65300'),('custom.flag','#FF205DB0'),('custom.text','#FF16733C'),('custom.empty','#FF586776')]))
        check('null remains visible as a typed value rather than an empty field',author()['fields']['custom.empty']['text']=='null')
        generation=app()['generation'];edit('custom.exact','2.345678901234567');wait('window.scadaApp.generation>'+str(generation))
        saved=saved_probe(export('precision.json'));check('native numeric edits retain the entered binary64 value in portable JSON',saved['custom']['exact']['number']==2.345678901234567 and app()['generation']==generation+1)
        command('undo');wait('!window.scadaApp.dirty');check('Undo restores the full original editor precision',author()['fields']['custom.exact']['text']=='1.23456789012345')
        search('custom.settings');original=author()['fields']['custom.settings']['text'];generation=app()['generation'];edit('custom.settings','{bad')
        wait('window.scadaApp.inspectorAuthoring.validation.length>0')
        check('malformed inline JSON is rejected without an unhandled exception or partial edit',app()['generation']==generation and author()['fields']['custom.settings']['text']==original and not app()['errors'])
        edit('custom.settings','{"target":2.345678901234567,"values":[false,null]}');wait('window.scadaApp.generation>'+str(generation))
        saved=saved_probe(export('structured-edit.json'));check('repairing inline structured text uses the ordinary revision-checked transaction',saved['custom']['settings']['fields']['target']['number']==2.345678901234567)
        command('undo');wait('!window.scadaApp.dirty');search('props.value')
        b=bounds(['inspectorAuthoring','bindings','props.value']);label=bounds(['inspectorAuthoring','labels','props.value']);value=bounds(['inspectorQualified','props.value'])
        check('native binding controls sit to the left of aligned property labels and values',b['x']+b['width']<=label['x']+1 and label['x']<value['x'])
        check('bound numeric fields retain read-only full precision',author()['fields']['props.value']['readOnly'] and author()['fields']['props.value']['text']=='1.23456789012345')
        updates=author()['boundTextUpdates'];tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+4))
        check('unchanged bound values do not reformat text on idle refreshes',author()['boundTextUpdates']==updates)
        open_creation();generation=app()['generation'];field('name','draftOnly');field('value','remembered string');choose('Object');field('value','{"draft":true}');choose('String')
        check('native type switching retains the separate string draft',creation()['text']=='remembered string')
        choose('Object');check('native type switching also restores the structured draft',creation()['text']=='{"draft":true}')
        page.keyboard.press('F5');page.keyboard.press('Control+d');tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+2))
        check('creation dialog keyboard scope blocks preview and canvas duplication',not app()['preview'] and app()['generation']==generation and app()['componentCount']==4)
        cancel();check('Cancel discards all typed drafts without adding history',app()['generation']==generation and not app()['dirty'])
        open_creation();field('name','exact');submit();wait('window.scadaApp.inspectorAuthoring.creation.error.length>0')
        check('duplicate property names are diagnosed before mutating the project',app()['generation']==generation and not app()['dirty'])
        field('name','createdObject');choose('Object');field('value','[]');submit();wait('window.scadaApp.inspectorAuthoring.creation.error.length>0')
        check('Object creation rejects an Array root instead of changing the chosen type',creation()['kind']=='Object' and app()['generation']==generation)
        field('value','{');submit();wait('window.scadaApp.inspectorAuthoring.creation.error.length>0')
        check('malformed creation JSON remains a recoverable local dialog error',creation()['error']!='' and app()['generation']==generation and not app()['errors'])
        field('value','{"precision":1.23456789012345,"items":[true,null,"ready"]}')
        page.screenshot(path=str(OUT/'typed-property-dialog-desktop.png'),full_page=True);submit();wait('window.scadaApp.inspectorAuthoring.creation===null && window.scadaApp.generation>'+str(generation))
        saved=saved_probe(export('object-created.json'))
        check('Object creation commits nested typed values in exactly one transaction',app()['generation']==generation+1 and saved['custom']['createdObject']['fields']['items']['items'][1]['kind']=='Null')
        check('successful creation preserves component selection and local property search',app()['selection']==['Probe'] and app()['inspectorFilter']['query']=='custom.')
        command('undo');wait('!window.scadaApp.dirty');command('redo');wait('window.scadaApp.dirty')
        check('created structured properties support Undo and Redo','createdObject' in saved_probe(export('object-redone.json'))['custom'])
        command('undo');wait('!window.scadaApp.dirty')
        for name,kind,text in [('createdArray','Array','[1,true,null,{"value":2.345678901234567}]'),('createdBoolean','Boolean','true'),('createdNull','Null',None),('createdNumber','Number','0.000000123456789')]:
            open_creation();field('name',name);choose(kind)
            if text is not None:field('value',text)
            generation=app()['generation'];submit();wait('window.scadaApp.inspectorAuthoring.creation===null && window.scadaApp.generation>'+str(generation))
            saved=saved_probe(export(name+'.json'))['custom'][name]
            check(kind+' creation preserves its selected tagged kind',saved['kind']==kind and app()['generation']==generation+1)
            command('undo');wait('!window.scadaApp.dirty')
        open_creation('props');field('name','localNote');field('value','props data');generation=app()['generation'];submit();wait('window.scadaApp.inspectorAuthoring.creation===null && window.scadaApp.generation>'+str(generation))
        saved=saved_probe(export('props-created.json'));check('component-property creation writes Props rather than the Custom category',saved['props']['localNote']['text']=='props data' and 'localNote' not in saved['custom'])
        command('undo');wait('!window.scadaApp.dirty');open_creation();field('name','nonfinite');choose('Number');field('value','1e999');generation=app()['generation'];submit();wait('window.scadaApp.inspectorAuthoring.creation.error.length>0')
        check('nonfinite number creation is rejected before publication',app()['generation']==generation and not app()['dirty']);cancel()
        page.set_viewport_size({'width':1024,'height':768});wait('window.scadaApp.viewport.width===1024');command('fit');select('Probe');search('custom.')
        page.screenshot(path=str(OUT/'typed-property-inspector-tablet.png'),full_page=True)
        open_creation();field('name','tabletArray');choose('Array');field('value','[true,null,3.141592653589793]');generation=app()['generation'];submit();wait('window.scadaApp.inspectorAuthoring.creation===null && window.scadaApp.generation>'+str(generation))
        saved=saved_probe(export('tablet-array.json'));check('tablet native creation commits a typed Array with unrounded values',saved['custom']['tabletArray']['items'][2]['number']==3.141592653589793)
        command('undo');wait('!window.scadaApp.dirty')
        check('all canceled rejected and undone authoring leaves the full exported project unchanged',export('final.json')==initial)
        check('typed authoring completes without unhandled browser or application errors',not app()['errors'] and not app()['diagnostics'] and not any(e['kind']=='pageerror' for e in logs))
    except Exception as error:
        results.append({'name':'typed property authoring suite','passed':False,'error':str(error)});page.screenshot(path=str(OUT/'failure.png'),full_page=True);raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'),indent=2));browser.close()
