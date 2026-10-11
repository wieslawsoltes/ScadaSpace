// Browser history adapter for local runtime pages. Only URLs/entry IDs cross this boundary.
(() => {
    let pending = null;
    const read = () => ({
        url: location.hash.slice(1) || '/',
        entry: typeof history.state?.scadaEntry === 'string' && history.state.scadaEntry.length <= 36 ? history.state.scadaEntry : ''
    });
    const capture = () => { pending = read(); };
    window.addEventListener('popstate', capture);
    window.addEventListener('hashchange', capture);
    window.scadaNavigation = Object.freeze({
        initial: () => read().url,
        take: () => { const value = pending; pending = null; return value ? JSON.stringify(value) : ''; },
        publish: change => {
            if (!change || !['Push', 'Replace', 'Traverse', 'Restore'].includes(change.kind)) throw new TypeError('Unknown navigation operation.');
            if (typeof change.url !== 'string' || !change.url.startsWith('/') || change.url.startsWith('//') || change.url.length > 4096) throw new TypeError('Invalid internal page URL.');
            if (change.kind === 'Traverse' && (!Number.isInteger(change.delta) || Math.abs(change.delta) !== 1)) throw new TypeError('Invalid navigation distance.');
            document.title = change.title;
            if (change.kind === 'Traverse') { history.go(change.delta); return; }
            if (change.kind === 'Restore') return;
            // Replacing this app's entry must not erase metadata owned by the embedding host.
            const state = change.kind === 'Replace' && history.state && typeof history.state === 'object' ? { ...history.state } : {};
            state.scadaEntry = change.entry;
            const url = location.pathname + location.search + '#' + change.url;
            if (change.kind === 'Push') history.pushState(state, '', url);
            else history.replaceState(state, '', url);
        }
    });
})();
