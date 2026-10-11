// Browser history adapter for local runtime pages. Only URLs/entry IDs cross this boundary.
(() => {
    let pending = null;
    const read = () => ({ url: location.hash.slice(1) || '/', entry: history.state?.scadaEntry || '' });
    const capture = () => { pending = read(); };
    window.addEventListener('popstate', capture);
    window.addEventListener('hashchange', capture);
    window.scadaNavigation = Object.freeze({
        initial: () => read().url,
        take: () => { const value = pending; pending = null; return value ? JSON.stringify(value) : ''; },
        publish: change => {
            document.title = change.title;
            if (change.kind === 'Traverse') { history.go(change.delta); return; }
            if (change.kind === 'Restore') return;
            const state = { scadaEntry: change.entry };
            const url = location.pathname + location.search + '#' + change.url;
            if (change.kind === 'Push') history.pushState(state, '', url);
            else history.replaceState(state, '', url);
        }
    });
})();
