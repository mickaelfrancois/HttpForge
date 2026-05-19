window.forge = window.forge || {};

window.forge.setCaret = (el, pos) => {
    if (!el) return;
    el.focus();
    if (typeof el.setSelectionRange === 'function') {
        try { el.setSelectionRange(pos, pos); } catch (e) { /* ignore */ }
    }
};

window.forge.attach = (el, dotnetRef) => {
    if (!el || el._forgeHandler) return;
    const handler = (e) => {
        if (!el._forgeOpen) return;
        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnNavigate', 1);
                break;
            case 'ArrowUp':
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnNavigate', -1);
                break;
            case 'Enter':
            case 'Tab':
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnSelectCurrent');
                break;
            case 'Escape':
                e.preventDefault();
                dotnetRef.invokeMethodAsync('OnEscape');
                break;
        }
    };
    el.addEventListener('keydown', handler);
    el._forgeHandler = handler;
    el._forgeOpen = false;
};

window.forge.detach = (el) => {
    if (!el || !el._forgeHandler) return;
    el.removeEventListener('keydown', el._forgeHandler);
    delete el._forgeHandler;
    delete el._forgeOpen;
};

window.forge.setOpen = (el, open) => {
    if (!el) return;
    el._forgeOpen = !!open;
};
