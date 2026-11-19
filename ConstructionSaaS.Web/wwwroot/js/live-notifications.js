(() => {
    const root = document.getElementById('liveNotifications');
    if (!root) return;
    const list = document.getElementById('liveNoticeItems');
    const count = document.getElementById('liveNoticeCount');
    const status = document.getElementById('liveNoticeConnection');
    const feedback = document.getElementById('liveNoticeFeedback');
    const all = document.getElementById('liveNoticeReadAll');
    const summary = root.querySelector('summary');
    let stream, stopped = false, signature, busy = false;

    function render(data) {
        const next = JSON.stringify(data);
        if (next === signature) return;
        signature = next;
        count.textContent = data.unread > 99 ? '99+' : data.unread;
        count.hidden = !data.unread;
        all.disabled = busy || !data.unread;
        summary.setAttribute('aria-label', `Notifications: ${data.unread} unread`);
        const focused = list.contains(document.activeElement) ? document.activeElement.dataset.key : null;
        list.replaceChildren();
        list.setAttribute('aria-busy', 'false');
        for (const n of data.items) {
            const row = document.createElement('article');
            row.className = n.isRead ? '' : 'unread';
            const message = document.createElement('a');
            message.textContent = n.message;
            message.dataset.key = `link-${n.id}`;
            // Only local, server-generated destinations are accepted.
            message.href = typeof n.url === 'string' && /^\/(?!\/)/.test(n.url) ? n.url : '#';
            const date = document.createElement('time');
            const timestamp = /(?:Z|[+-]\d\d:\d\d)$/.test(n.createdAtUtc) ? n.createdAtUtc : n.createdAtUtc + 'Z';
            date.dateTime = timestamp;
            date.textContent = new Date(timestamp).toLocaleString();
            row.append(message, date);
            if (!n.isRead) {
                const button = document.createElement('button');
                button.type = 'button'; button.textContent = 'Mark as read';
                button.dataset.key = `read-${n.id}`;
                button.disabled = busy;
                button.onclick = () => update(root.dataset.readUrl, { id: n.id });
                row.append(button);
            }
            list.append(row);
        }
        if (!data.items.length) {
            const empty = document.createElement('p');
            empty.className = 'notification-empty';
            empty.textContent = 'You are up to date. Project and task updates will appear here.';
            list.append(empty);
        }
        if (focused) {
            const target = [...list.querySelectorAll('[data-key]')].find(e => e.dataset.key === focused);
            (target || summary).focus({ preventScroll: true });
        }
    }
    async function update(url, values = {}) {
        if (busy) return;
        busy = true;
        root.querySelectorAll('button').forEach(b => b.disabled = true);
        try {
            const body = new URLSearchParams({ ...values, __RequestVerificationToken: root.querySelector('input[name="__RequestVerificationToken"]').value });
            const response = await fetch(url, { method: 'POST', body });
            if (!response.ok) throw Error();
            feedback.textContent = values.id ? 'Notification marked as read.' : 'All notifications marked as read.';
            const snapshot = await fetch(root.dataset.inboxUrl, { cache: 'no-store' });
            if (!snapshot.ok) throw Error();
            busy = false; signature = null; render(await snapshot.json());
        } catch {
            feedback.textContent = 'Could not refresh the inbox. Please try again.';
        } finally {
            busy = false;
            root.querySelectorAll('button').forEach(b => b.disabled = false);
            all.disabled = count.hidden;
        }
    }
    function connect() {
        if (stream || stopped || document.hidden || !navigator.onLine) return;
        stream = new EventSource(root.dataset.streamUrl);
        stream.onmessage = event => {
            try { render(JSON.parse(event.data)); status.textContent = 'Live updates connected'; }
            catch { status.textContent = 'Could not load updates. Reconnecting...'; }
        };
        stream.onerror = () => { status.textContent = navigator.onLine ? 'Reconnecting...' : 'Offline - reconnecting when online'; };
        stream.addEventListener('unavailable', () => { status.textContent = 'Updates temporarily unavailable - retrying...'; });
        stream.addEventListener('unauthorized', () => {
            stopped = true; stream.close(); stream = null;
            signature = null; render({ unread: 0, items: [] });
            status.textContent = 'Session ended. Sign in again for updates.';
        });
    }
    all.addEventListener('click', () => update(root.dataset.readAllUrl));
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) { stream?.close(); stream = null; } else connect();
    });
    window.addEventListener('offline', () => {
        stream?.close(); stream = null;
        if (!stopped) status.textContent = 'Offline - reconnecting when online';
    });
    window.addEventListener('online', () => {
        if (stopped) return;
        status.textContent = 'Reconnecting...';
        connect();
    });
    window.addEventListener('pagehide', () => { stream?.close(); stream = null; });
    window.addEventListener('pageshow', connect);
    document.addEventListener('click', e => { if (!root.contains(e.target)) root.open = false; });
    document.addEventListener('keydown', e => { if (e.key === 'Escape' && root.open) { root.open = false; summary.focus(); } });
    connect();
})();
