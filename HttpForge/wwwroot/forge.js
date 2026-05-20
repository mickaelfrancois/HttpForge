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

window.forge.theme = {
    get: () => localStorage.getItem('forge-theme') || 'system',
    set: (mode) => {
        if (mode === 'light' || mode === 'dark') {
            localStorage.setItem('forge-theme', mode);
            document.documentElement.setAttribute('data-theme', mode);
        } else {
            localStorage.removeItem('forge-theme');
            document.documentElement.removeAttribute('data-theme');
        }
    }
};

window.forge.sidebar = {
    init(sidebarEl, handleEl) {
        if (!sidebarEl || !handleEl) return;
        if (handleEl._forgeInitialized) return;
        handleEl._forgeInitialized = true;

        const stored = localStorage.getItem('forge-sidebar-width');
        if (stored) {
            const w = Math.min(500, Math.max(200, parseInt(stored, 10)));
            if (!isNaN(w)) sidebarEl.style.width = w + 'px';
        }

        let startX = 0, startWidth = 0;

        const onMouseMove = (e) => {
            const newWidth = Math.min(500, Math.max(200, startWidth + (e.clientX - startX)));
            sidebarEl.style.width = newWidth + 'px';
        };

        const onMouseUp = () => {
            const w = parseInt(sidebarEl.style.width, 10) || 320;
            localStorage.setItem('forge-sidebar-width', w);
            handleEl.classList.remove('dragging');
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);
            document.body.style.cursor = '';
            document.body.style.userSelect = '';
        };

        handleEl.addEventListener('mousedown', (e) => {
            e.preventDefault();
            startX = e.clientX;
            startWidth = sidebarEl.offsetWidth;
            handleEl.classList.add('dragging');
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
            document.addEventListener('mousemove', onMouseMove);
            document.addEventListener('mouseup', onMouseUp);
        });
    }
};
