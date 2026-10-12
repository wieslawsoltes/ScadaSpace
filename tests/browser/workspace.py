"""Real Uno designer workspace input. Observations never invoke managed commands."""
from __future__ import annotations
import json
import os
from pathlib import Path
from playwright.sync_api import sync_playwright

OUT = Path('TestResults/browser-workspace'); OUT.mkdir(parents=True, exist_ok=True)
BASE = os.environ.get('SCADASPACE_URL', 'http://127.0.0.1:8765/ScadaSpace/')
results, logs = [], []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, args=['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--disable-dev-shm-usage'])
    context = browser.new_context(viewport={'width':1600,'height':1000}, accept_downloads=True)
    page = context.new_page()
    page.on('pageerror', lambda e: logs.append({'kind':'pageerror','text':str(e)}))
    page.on('console', lambda e: logs.append({'kind':e.type,'text':e.text}))
    def app(): return page.evaluate('window.scadaApp')
    def workspace(): return app()['workspace']
    def wait(js): page.wait_for_function(js, timeout=30000)
    def bounds(name): return app()['controls'].get(name)
    def click(box, button='left'):
        assert box and box['width']>0 and box['height']>0, 'Missing native geometry'
        page.mouse.click(box['x']+box['width']/2, box['y']+box['height']/2, button=button)
    def command(name):
        page.wait_for_function('key=>window.scadaApp.controls[key]?.width>0', arg=name, timeout=30000)
        click(bounds(name)); page.wait_for_timeout(350)
    def fill(name, text):
        command(name); page.keyboard.press('Control+a'); page.keyboard.press('Backspace')
        if text: page.keyboard.insert_text(text)
        page.wait_for_timeout(450)
    def check(name, ok):
        results.append({'name':name,'passed':bool(ok)})
        if not ok: raise AssertionError(name)
        print('PASS',name,flush=True)
    def panel(key):
        command('panel-chooser'); command('panel-menu-'+key+'-1')
    def count_nodes(node): return 1+sum(count_nodes(child) for child in node.get('children',[]))
    def tab_menu(path, title):
        click(bounds('view-'+path), button='right'); page.wait_for_timeout(350)
        command('tab-menu-'+path+':'+title)
    try:
        page.goto(BASE,wait_until='domcontentloaded',timeout=120000)
        page.wait_for_function('window.scadaApp?.workspace && window.scadaApp.tick>3',timeout=120000)
        command('fit')
        check('designer uses compact commands and bottom resource tabs', bounds('save')['height']<=28 and bounds('view-Overview')['y']>900 and bounds('save')['y']<60)
        check('component palette defaults to auto-hide beside the property editor', not workspace()['paletteVisible'] and bounds('components-rail') is not None and bounds('palette-search') is None and bounds('property-pane-close') is not None)
        check('workspace opens all existing views without changing the project', len(workspace()['open'])==5 and workspace()['active']=='Overview' and not app()['dirty'])
        page.screenshot(path=str(OUT/'designer-workspace.png'),full_page=True)
        identity=page.evaluate('window.scadaViews[0].id'); created=workspace()['tabsCreated']; explorer=workspace()['explorerNodesCreated']
        for _ in range(4): command('view-Overview')
        check('activating the current resource retains its actual view instance and native tabs', page.evaluate('window.scadaViews[0].id')==identity and workspace()['tabsCreated']==created and workspace()['explorerNodesCreated']==explorer)
        command('components-rail'); wait('window.scadaApp.workspace.paletteVisible')
        count=workspace()['paletteButtonsCreated']
        fill('palette-search','Tank')
        check('palette search reveals matching components without recreating buttons', workspace()['paletteVisibleCount']==1 and workspace()['paletteButtonsCreated']==count==35 and bounds('palette-item-Tank') is not None)
        fill('palette-search',''); command('palette-collapse')
        check('palette category collapse hides native component rows', workspace()['paletteVisibleCount']==0)
        fill('palette-search','Label')
        check('search temporarily reveals a matching collapsed category', workspace()['paletteVisibleCount']==1 and bounds('palette-item-Label') is not None)
        fill('palette-search','')
        check('clearing search restores category expansion choices', workspace()['paletteVisibleCount']==0)
        command('palette-expand'); command('palette-mode')
        check('tile display reuses every existing component button', workspace()['panels']['paletteTiles'] and workspace()['paletteButtonsCreated']==count and workspace()['paletteVisibleCount']==35)
        command('components-pane-pin')
        check('pinning reserves a persistent component pane and hides its edge rail', workspace()['panels']['componentsPinned'] and workspace()['paletteVisible'] and bounds('components-rail') is None and bounds('property-pane-close')['y']>bounds('components-pane-pin')['y'])
        page.screenshot(path=str(OUT/'pinned-palette.png'),full_page=True)
        command('project-pane-close')
        check('hiding Project Browser leaves Tag Browser available', not workspace()['panels']['projectBrowser'] and bounds('project-filter') is None and bounds('tags-pane-close') is not None)
        panel('project')
        check('Panel Chooser restores hidden panels through native menu input', workspace()['panels']['projectBrowser'] and bounds('project-filter') is not None)
        initial_width=workspace()['panels']['leftWidth']
        grip=bounds('left-splitter'); x=grip['x']+grip['width']/2; y=grip['y']+grip['height']/2
        page.mouse.move(x,y);page.mouse.down();page.mouse.move(x+50,y,steps=8);page.mouse.up()
        wait('Math.abs(window.scadaApp.workspace.panels.leftWidth - '+str(initial_width+50)+') < 1')
        saved_width=workspace()['panels']['leftWidth']; tick=app()['tick']; wait('window.scadaApp.tick>'+str(tick+2))
        check('splitter changes remain workspace-only rather than project edits', not app()['dirty'] and saved_width>270)
        page.reload(wait_until='domcontentloaded'); page.wait_for_function('window.scadaApp?.workspace && window.scadaApp.tick>3',timeout=120000)
        check('panel widths pinning and display mode survive full browser reload', abs(workspace()['panels']['leftWidth']-saved_width)<1 and workspace()['panels']['componentsPinned'] and workspace()['panels']['paletteTiles'])
        command('menu-View'); command('menu-View-Reset Panels')
        check('View Reset Panels restores the default auto-hidden layout', not workspace()['panels']['componentsPinned'] and not workspace()['paletteVisible'] and workspace()['panels']['leftWidth']==235 and not workspace()['panels']['paletteTiles'])
        command('fit'); tank=app()['components']['Tank01']; click(tank['bounds']); page.wait_for_timeout(250)
        check('designer selection still reaches the original component', app()['selection']==['Tank01'])
        page.screenshot(path=str(OUT/'designer-selected.png'),full_page=True)
        generation=app()['generation']; components=app()['componentCount']; explorer=workspace()['explorerNodesCreated']
        fill('project-filter','Trends'); page.keyboard.press('Delete'); page.wait_for_timeout(350)
        check('project filtering and text deletion cannot delete the selected component', app()['generation']==generation and app()['componentCount']==components and workspace()['explorerNodesCreated']==explorer)
        fill('project-filter',''); command('components-rail'); fill('palette-search','Label'); command('palette-item-Label')
        wait('window.scadaApp.componentCount>'+str(components))
        check('filtered palette insertion is a real undoable designer edit', app()['componentCount']==components+1 and app()['dirty'] and app()['canUndo'])
        command('undo'); wait('window.scadaApp.componentCount=='+str(components))
        check('undo restores the document without recreating unaffected resource tabs', not app()['dirty'] and workspace()['tabsCreated']==5)
        command('redo'); wait('window.scadaApp.componentCount=='+str(components+1))
        command('components-rail')
        command('close-view-Overview')
        check('closing an edited resource keeps its unsaved project content', 'Overview' not in workspace()['open'] and app()['dirty'] and app()['componentCount']==components+1 and workspace()['active']!='Overview')
        active=workspace()['active']; tab_menu(active,'Close Others')
        check('resource context menu closes other tabs without deleting views', workspace()['open']==[active] and app()['componentCount']==components+1)
        tab_menu(active,'Close All'); wait('window.scadaApp.workspace.active===null')
        check('closing all resources leaves a usable empty workspace', workspace()['open']==[] and bounds('reopen-view') is not None and app()['components']=={} and not app()['errors'])
        with page.expect_download() as event: command('export')
        event.value.save_as(OUT/'closed-tabs-export.json'); exported=json.loads((OUT/'closed-tabs-export.json').read_text())
        check('export after closing all tabs retains all project views and unsaved components', len(exported['views'])==5 and sum(count_nodes(v['root']) for v in exported['views'])>=components+1)
        command('reopen-view'); wait("window.scadaApp.workspace.active==='Overview'")
        check('reopening a resource restores its modified design', app()['componentCount']==components+1 and app()['dirty'] and app()['components'])
        command('undo'); wait('window.scadaApp.componentCount=='+str(components))
        command('fit'); page.set_viewport_size({'width':1024,'height':768}); page.wait_for_timeout(500); command('fit')
        check('tablet layout keeps commands resources and property editor usable', app()['viewport']['width']==1024 and bounds('view-Overview')['y']>650 and bounds('project-filter') is None and bounds('property-pane-close') is not None)
        page.screenshot(path=str(OUT/'workspace-tablet.png'),full_page=True)
        check('workspace interactions finish without managed or browser exceptions', not app()['errors'] and not app()['diagnostics'] and not any(e['kind']=='pageerror' for e in logs))
    except Exception as error:
        results.append({'name':'workspace browser run','passed':False,'error':str(error)})
        page.screenshot(path=str(OUT/'failure.png'),full_page=True)
        raise
    finally:
        (OUT/'results.json').write_text(json.dumps(results,indent=2))
        (OUT/'console.json').write_text(json.dumps(logs,indent=2))
        (OUT/'last-state.json').write_text(json.dumps(page.evaluate('({app:window.scadaApp,views:window.scadaViews})'),indent=2))
        browser.close()
