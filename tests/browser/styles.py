"""Native style class authoring and rendered cascade regression definitions.
Requires a release-built Uno app; this source file does not imply execution success.
"""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path('TestResults/browser-styles'); OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get('SCADASPACE_URL', 'http://127.0.0.1:8765/ScadaSpace/')
results, logs = [], []
def scalar(v):
    if isinstance(v, bool): return {'kind':'Boolean','boolean':v}
    if isinstance(v, (int, float)): return {'kind':'Number','number':v}
    if isinstance(v, str): return {'kind':'String','text':v}
    return {'kind':'Null'}
def node(name, y, classes, inline=None):
    return {'id':name,'name':name,'type':'Label','x':60,'y':y,'width':850,'height':100,
            'props':{'text':scalar(name+' · process heading'),'fontSize':scalar(14),'foreground':scalar('#333333')},
            'styleClasses':classes,'style':{k:scalar(v) for k,v in (inline or {}).items()}}
styles = {'Alpha':{'foreground':'#224466','fontSize':22},
          'Zulu':{'foreground':'#AA2233','background':'#F4EDEE','borderColor':'#888888','borderWidth':2}}
root={'id':'root','name':'Root','type':'Container','x':0,'y':0,'width':1000,'height':650,
      'children':[node('Styled',70,['Zulu','Alpha']),node('Inline',230,['Zulu','Alpha'],{'foreground':'#117744','opacity':.5,'fontWeight':'Bold'}),node('Unassigned',390,[])]}
fixture={'schemaVersion':1,'name':'Style Class Lab','views':[{'path':'Main','width':1000,'height':650,'root':root}],
         'pages':[{'path':'/','view':'Main'}],'styles':{n:{k:scalar(v) for k,v in s.items()} for n,s in styles.items()},'tags':[],'alarms':[]}
fixture_file=OUT/'fixture.json'; fixture_file.write_text(json.dumps(fixture))
with sync_playwright() as p:
    browser=p.chromium.launch(headless=True,args=['--use-angle=swiftshader','--enable-unsafe-swiftshader','--disable-dev-shm-usage'])
    context=browser.new_context(viewport={'width':1600,'height':1000},accept_downloads=True)
    page=context.new_page()
    page.on('pageerror',lambda e:logs.append({'kind':'pageerror','text':str(e)}))
    page.on('console',lambda e:logs.append({'kind':e.type,'text':e.text}))
    def app():return page.evaluate('window.scadaApp')
    def editor():return app()['styleEditor']
    def wait(js):page.wait_for_function(js,timeout=30000)
    def click(b):
        assert b and b['width']>0 and b['height']>0,'Native control is not arranged'
        page.mouse.click(b['x']+b['width']/2,b['y']+b['height']/2)
    def command(name):
        page.wait_for_function('n=>window.scadaApp.controls[n]?.width>0',arg=name,timeout=30000)
        click(app()['controls'][name])
    def field(name,text=None):
        page.wait_for_function('n=>window.scadaApp.styleEditor?.fields[n]?.width>0',arg=name,timeout=30000)
        click(editor()['fields'][name])
        if text is not None:
            page.keyboard.press('Control+a');page.keyboard.press('Backspace')
            if text:page.keyboard.insert_text(text)
    def choose(name):
        page.wait_for_function('n=>window.scadaApp.styleEditor?.rows?.some(r=>r.name===n&&r.bounds?.width>0)',arg=name,timeout=30000)
        click(next(r['bounds'] for r in editor()['rows'] if r['name']==name))
        wait('window.scadaApp.styleEditor.selected==='+json.dumps(name))
    def auxiliary(prop,name,text=None):
        page.wait_for_function('([p,n])=>window.scadaApp.styleEditor?.inputs[p]?.controls[n]?.width>0',arg=[prop,name],timeout=30000)
        click(editor()['inputs'][prop]['controls'][name])
        if text is not None:
            page.keyboard.press('Control+a');page.keyboard.press('Backspace')
            if text:page.keyboard.insert_text(text)
    def option(prop,name):
        field(prop)
        page.wait_for_function('([p,n])=>window.scadaApp.styleEditor.inputs[p]?.options?.some(i=>i.name===n&&i.bounds?.width>0)',arg=[prop,name],timeout=30000)
        click(next(v['bounds'] for v in editor()['inputs'][prop]['options'] if v['name']==name))
        page.wait_for_function('p=>window.scadaApp.styleEditor.inputs[p].options===null',arg=prop,timeout=30000)
    def native(name):return app()['components'][name]['nativeStyle']
    def open_editor():
        command('menu-Project');command('style-classes');wait('window.scadaApp.styleEditor!==null')
    def check(name,value):
        results.append({'name':name,'passed':bool(value)})
        if not value:raise AssertionError(name)
        print('PASS',name,flush=True)
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000)
        page.wait_for_function('window.scadaApp?.ready && window.scadaApp.tick>2',timeout=120000)
        with page.expect_file_chooser() as chosen:command('import')
        chosen.value.set_files(fixture_file);wait("window.scadaApp.project==='Style Class Lab'");command('fit')
        wait("window.scadaApp.components.Styled?.nativeStyle?.foreground==='#FFAA2233'")
        check('rendered text follows alphabetical class precedence despite reversed assignment order',native('Styled')['foreground']=='#FFAA2233' and native('Styled')['fontSize']==22)
        check('inline color and font weight override classes on actual native controls',native('Inline')['foreground']=='#FF117744' and native('Inline')['fontWeight']==700)
        check('opacity applies to the decorated frame including its background',native('Inline')['opacity']==.5)
        click(app()['components']['Styled']['bounds']);wait("window.scadaApp.selection[0]==='Styled'");open_editor()
        check('Project menu opens the native style class list and grouped editor',editor()['selected']=='Alpha' and editor()['classes']==['Alpha','Zulu'])
        choose('Zulu');field('foreground','#775511');wait("window.scadaApp.styleEditor.previewForeground==='#FF775511'")
        check('valid style field changes update the native preview without changing the document',native('Styled')['foreground']=='#FFAA2233' and not app()['dirty'])
        option('fontWeight','Bold');wait('window.scadaApp.styleEditor.previewFontWeight===700')
        check('native font-weight choices update only the staged preview',not app()['dirty'] and editor()['inputs']['fontWeight']['text']=='Bold')
        auxiliary('fontWeight','reset');wait('window.scadaApp.styleEditor.previewFontWeight===400')
        check('inherit reset removes an explicitly selected font weight',editor()['inputs']['fontWeight']['text']=='')
        option('italic','true');wait("window.scadaApp.styleEditor.previewFontStyle==='Italic'")
        check('native italic choice preserves a typed Boolean rather than a text style',editor()['inputs']['italic']['text']=='true')
        auxiliary('italic','reset');wait("window.scadaApp.styleEditor.previewFontStyle==='Normal'")
        auxiliary('foreground','color');wait('window.scadaApp.styleEditor.inputs.foreground.colorOpen')
        auxiliary('foreground','color-text','#80446688')
        auxiliary('foreground','color-cancel');wait('!window.scadaApp.styleEditor.inputs.foreground.colorOpen')
        check('color flyout cancellation preserves the prior class field',editor()['previewForeground']=='#FF775511' and editor()['inputs']['foreground']['text']=='#775511')
        auxiliary('foreground','color');wait('window.scadaApp.styleEditor.inputs.foreground.colorOpen')
        auxiliary('foreground','color-text','#80GG2233');auxiliary('foreground','color-use')
        wait('window.scadaApp.styleEditor.inputs.foreground.colorError.length>0')
        check('invalid color values cannot be accepted or leak into the class',editor()['inputs']['foreground']['colorOpen'] and editor()['inputs']['foreground']['text']=='#775511')
        auxiliary('foreground','color-text','#80446688');auxiliary('foreground','color-use')
        wait("!window.scadaApp.styleEditor.inputs.foreground.colorOpen && window.scadaApp.styleEditor.previewForeground==='#80446688'")
        check('color confirmation retains ARGB alpha without committing the project',not app()['dirty'] and editor()['inputs']['foreground']['text']=='#80446688')
        field('foreground','#775511');wait("window.scadaApp.styleEditor.previewForeground==='#FF775511'")
        field('group-Text');wait('window.scadaApp.styleEditor.fields.foreground===null')
        check('collapsing a property group retains all staged field values',editor()['inputs']['foreground']['text']=='#775511')
        field('group-Text');wait('window.scadaApp.styleEditor.fields.foreground!==null')
        field('opacity','2');wait("window.scadaApp.styleEditor.error.length>0")
        generation=app()['generation'];command('configuration-dialog-ok');wait('window.scadaApp.styleEditor!==null && window.scadaApp.styleEditor.error.length>0')
        check('invalid opacity blocks Apply without partially changing the class',app()['generation']==generation and not app()['dirty'])
        check('an invalid style is not presented using a stale preview',not editor()['previewVisible'])
        field('opacity','.75');wait('window.scadaApp.styleEditor.previewOpacity===.75')
        command('configuration-dialog-apply');wait('window.scadaApp.generation>'+str(generation))
        check('Apply updates all class users while preserving inline overrides',native('Styled')['foreground']=='#FF775511' and native('Styled')['opacity']==.75 and native('Inline')['foreground']=='#FF117744' and native('Inline')['opacity']==.5)
        generation=app()['generation'];command('configuration-dialog-apply');tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+2))
        check('unchanged Apply creates no additional undo record',app()['generation']==generation)
        page.screenshot(path=str(OUT/'style-editor.png'),full_page=True)
        field('name','Theme/Heading');field('rename');wait("window.scadaApp.styleEditor.selected==='Theme/Heading'")
        generation=app()['generation'];command('configuration-dialog-apply');wait('window.scadaApp.generation>'+str(generation))
        check('rename updates class references in one transaction without changing rendered output',native('Styled')['foreground']=='#FF775511' and editor()['classes']==['Alpha','Theme/Heading'])
        field('foreground','#FFFFFF');wait("window.scadaApp.styleEditor.previewForeground==='#FFFFFFFF'")
        generation=app()['generation'];command('configuration-dialog-cancel');wait('window.scadaApp.styleEditor===null')
        check('Cancel discards unapplied fields but retains the preceding Apply',app()['generation']==generation and native('Styled')['foreground']=='#FF775511')
        command('undo');wait('window.scadaApp.generation>'+str(generation));open_editor()
        check('Undo restores the original class name and its references',editor()['classes']==['Alpha','Zulu'])
        command('configuration-dialog-cancel');wait('window.scadaApp.styleEditor===null');generation=app()['generation'];command('redo');wait('window.scadaApp.generation>'+str(generation))
        open_editor();choose('Theme/Heading');field('name','Theme/Copy');field('duplicate');wait("window.scadaApp.styleEditor.selected==='Theme/Copy'")
        check('Duplicate creates a detached style with the same preview values',editor()['previewForeground']=='#FF775511' and 'Theme/Copy' in editor()['classes'])
        field('assigned');generation=app()['generation'];command('configuration-dialog-ok');wait('window.scadaApp.styleEditor===null && window.scadaApp.generation>'+str(generation))
        check('assignment changes participate in the document undo history',app()['dirty'] and app()['canUndo'])
        with page.expect_download() as downloaded:command('export')
        downloaded.value.save_as(OUT/'styles-export.json');exported=json.loads((OUT/'styles-export.json').read_text())
        label=next(n for n in exported['views'][0]['root']['children'] if n['name']=='Styled')
        check('export preserves renamed and duplicated styles plus component assignments','Theme/Heading' in exported['styles'] and 'Theme/Copy' in exported['styles'] and 'Zulu' not in label['styleClasses'] and 'Theme/Copy' in label['styleClasses'])
        open_editor();choose('Theme/Copy');field('delete');wait("!window.scadaApp.styleEditor.classes.includes('Theme/Copy')")
        generation=app()['generation'];command('configuration-dialog-apply');wait('window.scadaApp.generation>'+str(generation))
        check('class deletion commits cleanly',not app()['errors'])
        command('configuration-dialog-cancel');wait('window.scadaApp.styleEditor===null')
        generation=app()['generation'];command('undo');wait('window.scadaApp.generation>'+str(generation));open_editor()
        check('undoing deletion restores the class inventory','Theme/Copy' in editor()['classes'])
        field('filter','Theme/Copy');wait('window.scadaApp.styleEditor.rows.length===1')
        check('style inventory filtering retains the underlying class definitions',len(editor()['classes'])==3 and editor()['rows'][0]['name']=='Theme/Copy')
        field('filter','');choose('Theme/Copy');field('rename-references');field('name','ReferenceTest');field('rename')
        wait("window.scadaApp.styleEditor.selected==='ReferenceTest'")
        generation=app()['generation'];command('configuration-dialog-apply');wait('window.scadaApp.generation>'+str(generation))
        command('configuration-dialog-cancel');wait('window.scadaApp.styleEditor===null')
        with page.expect_download() as downloaded:command('export')
        downloaded.value.save_as(OUT/'reference-preserving-rename.json')
        renamed=json.loads((OUT/'reference-preserving-rename.json').read_text())
        assigned=next(n for n in renamed['views'][0]['root']['children'] if n['name']=='Styled')['styleClasses']
        check('reference-preserving rename keeps old assignment names as an explicit mode','ReferenceTest' in renamed['styles'] and 'Theme/Copy' not in renamed['styles'] and 'Theme/Copy' in assigned and 'ReferenceTest' not in assigned)
        generation=app()['generation'];command('undo');wait('window.scadaApp.generation>'+str(generation));open_editor()
        check('undo restores the class removed by reference-preserving rename','Theme/Copy' in editor()['classes'] and 'ReferenceTest' not in editor()['classes'])
        command('configuration-dialog-cancel');wait('window.scadaApp.styleEditor===null');command('preview');wait('window.scadaApp.preview')
        tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+4))
        check('runtime refresh cannot overwrite explicit class text formatting',native('Styled')['foreground']=='#FF775511' and native('Styled')['fontSize']==22)
        page.set_viewport_size({'width':1024,'height':768});wait('window.scadaApp.viewport.width===1024');command('fit');tick=app()['tick'];wait('window.scadaApp.tick>'+str(tick+2))
        check('styled native controls remain rendered at tablet width',native('Styled')['foreground']=='#FF775511' and app()['components']['Styled']['bounds']['width']>0)
        page.screenshot(path=str(OUT/'style-runtime-tablet.png'),full_page=True)
        check('style interactions finish without unhandled browser or managed errors',not app()['errors'] and not app()['diagnostics'] and not any(x['kind']=='pageerror' for x in logs))
    except Exception as error:
        results.append({'name':'style browser run','passed':False,'error':str(error)})
        page.screenshot(path=str(OUT/'failure.png'),full_page=True);raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2));(OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('window.scadaApp||null'),indent=2));browser.close()
