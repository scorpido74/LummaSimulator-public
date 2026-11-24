(() => {
    'use strict';
    const navigation = document.getElementById('presenter-navigation');
    const toggle = document.getElementById('presenter-collapse');
    const summary = document.getElementById('presenter-navigation-summary');
    if (!navigation || !toggle || !summary || navigation.dataset.attached) return;
    navigation.dataset.attached = 'true';
    const desktop = window.matchMedia('(min-width: 1024px)');
    toggle.hidden = false;
    const setExpanded = expanded => {
        toggle.setAttribute('aria-expanded', String(expanded));
        toggle.textContent = expanded ? '<<' : '>>';
    };
    const resize = () => { navigation.open = desktop.matches; document.body.classList.remove('sidebar-compact'); setExpanded(true); };
    toggle.addEventListener('click', () => {
        if (!desktop.matches) return;
        const compact = document.body.classList.toggle('sidebar-compact');
        setExpanded(!compact);
    });
    navigation.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !desktop.matches && navigation.open) { navigation.open = false; summary.focus(); }
    });
    desktop.addEventListener('change', resize);
    resize();
})();
