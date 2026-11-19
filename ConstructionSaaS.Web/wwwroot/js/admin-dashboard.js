(() => {
    const search = document.getElementById('projectSearch');
    const status = document.getElementById('projectStatus');
    const rows = [...document.querySelectorAll('.project-data-row')];
    const empty = document.getElementById('searchEmptyRow');
    const result = document.getElementById('filterResult');
    const account = document.getElementById('accountMenu');
    function filterProjects() {
        const term = search.value.trim().toLowerCase();
        let count = 0;
        rows.forEach(row => {
            row.hidden = !row.dataset.search.toLowerCase().includes(term) || (status.value !== '' && row.dataset.status.toLowerCase() !== status.value.toLowerCase());
            if (!row.hidden) count++;
        });
        empty.hidden = rows.length === 0 || count > 0;
        result.textContent = `${count} of ${rows.length} projects shown`;
    }
    search.addEventListener('input', filterProjects);
    status.addEventListener('change', filterProjects);
    document.getElementById('resetFilters').addEventListener('click', () => {
        search.value = '';
        status.value = '';
        filterProjects();
        search.focus();
    });
    document.addEventListener('click', event => {
        if (!account.contains(event.target)) account.open = false;
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && account.open) {
            account.open = false;
            account.querySelector('summary').focus();
        }
    });
    account.addEventListener('focusout', event => {
        if (!account.contains(event.relatedTarget)) account.open = false;
    });
    document.querySelectorAll('.sidebar .nav-link').forEach(link => {
        link.setAttribute('aria-label', link.textContent.trim());
        link.title = link.textContent.trim();
    });
})();
