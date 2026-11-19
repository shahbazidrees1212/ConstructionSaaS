(() => {
    const sidebar = document.querySelector('.sidebar, .manager-sidebar, .workspace-sidebar');
    if (!sidebar) return;
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'workspace-menu-button';
    button.textContent = 'Workspace navigation +';
    button.setAttribute('aria-expanded', 'false');
    sidebar.id ||= 'workspace-navigation';
    button.setAttribute('aria-controls', sidebar.id);
    const brand = sidebar.querySelector('.brand, .manager-brand, .workspace-brand');
    (brand || sidebar.firstElementChild)?.after(button);
    sidebar.classList.add('ui-nav-collapsed');
    button.addEventListener('click', () => {
        const expanded = sidebar.classList.toggle('ui-nav-collapsed') === false;
        button.setAttribute('aria-expanded', String(expanded));
        button.textContent = expanded ? 'Close navigation −' : 'Workspace navigation +';
    });
    sidebar.querySelectorAll('a.active, a.selected').forEach(link => link.setAttribute('aria-current', 'page'));
    if (sidebar.classList.contains('manager-sidebar')) {
        const paths = {
            overview: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
            project: 'M3 7V5h6l2 2h10v13H3z',
            schedule: 'M4 5h16v16H4z M8 3v4 M16 3v4 M4 10h16',
            document: 'M6 3h8l4 4v14H6z M14 3v5h4 M9 12h6 M9 16h6',
            report: 'M4 3v18h17 M8 16v-5 M13 16V7 M18 16v-8',
            history: 'M4 6a9 9 0 1 1-1 10 M3 3v5h5 M12 7v5l3 2',
            profile: 'M8 7a4 4 0 1 0 8 0a4 4 0 1 0-8 0 M4 21v-2a8 8 0 0 1 16 0v2',
            task: 'M9 5h12 M9 12h12 M9 19h12 M2 5l2 2 3-4 M2 12l2 2 3-4 M2 19l2 2 3-4',
            notification: 'M6 9a6 6 0 0 1 12 0v6l2 3H4l2-3z M10 21h4'
        };
        sidebar.querySelectorAll('nav a').forEach(link => {
            const text = link.textContent.toLowerCase();
            const kind = /notification/.test(text) ? 'notification' : /profile/.test(text) ? 'profile' : /history|activity/.test(text) ? 'history' : /schedule/.test(text) ? 'schedule' : /document/.test(text) ? 'document' : /report|progress/.test(text) ? 'report' : /task|assignment/.test(text) ? 'task' : /project/.test(text) ? 'project' : 'overview';
            link.querySelector('span[aria-hidden]')?.remove();
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('viewBox', '0 0 24 24');
            svg.setAttribute('class', 'workspace-nav-icon');
            svg.setAttribute('aria-hidden', 'true');
            const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            path.setAttribute('d', paths[kind]);
            svg.append(path); link.prepend(svg);
        });
    }
})();
