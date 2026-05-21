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
            const parsed = parseInt(stored, 10);
            if (!isNaN(parsed)) sidebarEl.style.width = Math.min(500, Math.max(200, parsed)) + 'px';
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

// MainLayout renders as Static SSR (no @rendermode) so OnAfterRenderAsync never fires.
// Initialize directly from JS — elements are in the DOM when this script loads.
(function () {
    const sidebar = document.querySelector('.forge-sidebar');
    const handle = document.querySelector('.forge-resize-handle');
    if (sidebar && handle) window.forge.sidebar.init(sidebar, handle);
}());

window.forge.scripts = {
    async run(script, response, vars) {
        const mutations = { request: {}, collection: {}, global: {} };
        const logs = [];

        const fg = {
            response: {
                status: response.status,
                statusText: response.statusText,
                headers: response.headers,
                body: response.body,
                json() {
                    if (!response.body) throw new Error('Response body is empty');
                    return JSON.parse(response.body);
                }
            },
            variables: {
                get(key) {
                    return vars.request?.[key] ?? vars.collection?.[key] ?? vars.global?.[key];
                },
                set(key, value, scope = 'collection') {
                    if (mutations[scope] === undefined) return;
                    mutations[scope][key] = String(value);
                }
            },
            console: {
                log(...args) { logs.push(args.map(a => typeof a === 'object' ? JSON.stringify(a) : String(a)).join(' ')); }
            }
        };

        try {
            const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
            await new AsyncFunction('fg', script)(fg);
            return { mutations, logs, error: null };
        } catch (e) {
            return { mutations, logs, error: e.message };
        }
    }
};

window.forge.editor = {
    _instances: new Map(),

    init(textareaEl, dotnetRef, initialValue, mode = 'javascript', hasBlur = false) {
        if (this._instances.has(textareaEl)) return;
        const isDark = document.documentElement.getAttribute('data-theme') === 'dark'
            || (!document.documentElement.getAttribute('data-theme')
                && window.matchMedia('(prefers-color-scheme: dark)').matches);
        const cm = CodeMirror.fromTextArea(textareaEl, {
            mode: mode,
            theme: isDark ? 'monokai' : 'default',
            lineNumbers: true,
            tabSize: 2,
            indentWithTabs: false,
            lineWrapping: false,
            autofocus: false
        });
        cm.setValue(initialValue || '');
        let _changeTimer = null;
        cm.on('change', () => {
            clearTimeout(_changeTimer);
            _changeTimer = setTimeout(() => {
                dotnetRef.invokeMethodAsync('OnScriptChanged', cm.getValue());
            }, 300);
        });
        if (hasBlur) {
            cm.on('blur', () => {
                dotnetRef.invokeMethodAsync('OnEditorBlur');
            });
        }
        this._instances.set(textareaEl, cm);
    },

    dispose(textareaEl) {
        const cm = this._instances.get(textareaEl);
        if (!cm) return;
        cm.toTextArea();
        this._instances.delete(textareaEl);
    }
};

window.forge.viewer = {
    _instances: new Map(),

    init(el, value) {
        if (this._instances.has(el)) return;
        const isDark = document.documentElement.getAttribute('data-theme') === 'dark'
            || (!document.documentElement.getAttribute('data-theme')
                && window.matchMedia('(prefers-color-scheme: dark)').matches);
        const cm = CodeMirror(el, {
            value: value || '',
            mode: 'application/json',
            theme: isDark ? 'monokai' : 'default',
            lineNumbers: true,
            readOnly: true,
            lineWrapping: false
        });
        this._instances.set(el, cm);
    },

    setValue(el, value) {
        const cm = this._instances.get(el);
        if (cm) cm.setValue(value || '');
    },

    dispose(el) {
        const cm = this._instances.get(el);
        if (!cm) return;
        this._instances.delete(el);
        cm.getWrapperElement().remove();
    }
};

window.forge.dnd = {
    _ref: null,
    _dragValue: null,
    _handlers: null,

    init(dotnetRef) {
        if (this._handlers) this.dispose();
        this._ref = dotnetRef;
        this._dragValue = null;

        const onDragStart = (e) => {
            const el = e.target.closest('[data-drag]');
            if (!el) return;
            this._dragValue = el.dataset.drag;
            e.dataTransfer.effectAllowed = 'move';
            e.dataTransfer.setData('text/plain', this._dragValue);
        };

        const onDragOver = (e) => {
            if (!this._dragValue) return;
            const el = e.target.closest('[data-drop]');
            if (!el) return;
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            document.querySelectorAll('.drag-over').forEach(x => x.classList.remove('drag-over'));
            el.classList.add('drag-over');
        };

        const onDragLeave = (e) => {
            const el = e.target.closest('[data-drop]');
            if (el && !el.contains(e.relatedTarget)) el.classList.remove('drag-over');
        };

        const onDragEnd = () => {
            document.querySelectorAll('.drag-over').forEach(x => x.classList.remove('drag-over'));
            this._dragValue = null;
        };

        const onDrop = (e) => {
            e.preventDefault();
            document.querySelectorAll('.drag-over').forEach(x => x.classList.remove('drag-over'));
            const el = e.target.closest('[data-drop]');
            if (!el || !this._dragValue || !this._ref) return;
            const dropValue = el.dataset.drop;
            const drag = this._dragValue;
            this._dragValue = null;
            this._ref.invokeMethodAsync('OnDrop', drag, dropValue).catch(() => {});
        };

        this._handlers = { onDragStart, onDragOver, onDragLeave, onDragEnd, onDrop };
        document.addEventListener('dragstart', onDragStart);
        document.addEventListener('dragover', onDragOver);
        document.addEventListener('dragleave', onDragLeave);
        document.addEventListener('dragend', onDragEnd);
        document.addEventListener('drop', onDrop);
    },

    dispose() {
        if (!this._handlers) return;
        const h = this._handlers;
        document.removeEventListener('dragstart', h.onDragStart);
        document.removeEventListener('dragover', h.onDragOver);
        document.removeEventListener('dragleave', h.onDragLeave);
        document.removeEventListener('dragend', h.onDragEnd);
        document.removeEventListener('drop', h.onDrop);
        this._handlers = null;
        this._ref = null;
        this._dragValue = null;
    }
};
