// Observes the real browser rendering contexts; does not alter renderer selection or security settings.
(() => {
  const state = window.scadaGraphics = { contexts: [], renderer: null, software: null, contextLosses: 0 };
  function observe(proto) {
    if (!proto || !proto.getContext || proto.getContext.scadaObserved) return;
    const original = proto.getContext;
    function getContext(type, ...args) {
      const context = original.call(this, type, ...args);
      if (context && !state.contexts.includes(type)) state.contexts.push(type);
      if (context && (type === 'webgl' || type === 'webgl2' || type === 'experimental-webgl')) {
        const debug = context.getExtension('WEBGL_debug_renderer_info');
        state.renderer = debug ? context.getParameter(debug.UNMASKED_RENDERER_WEBGL) : context.getParameter(context.RENDERER);
        state.software = /swiftshader|llvmpipe|softpipe|software/i.test(state.renderer) ? true : null;
        if (!this.scadaContextObserved) {
          this.scadaContextObserved = true;
          this.addEventListener?.('webglcontextlost', () => state.contextLosses++);
        }
      }
      return context;
    }
    getContext.scadaObserved = true;
    proto.getContext = getContext;
  }
  observe(window.HTMLCanvasElement?.prototype);
  observe(window.OffscreenCanvas?.prototype);
})();
