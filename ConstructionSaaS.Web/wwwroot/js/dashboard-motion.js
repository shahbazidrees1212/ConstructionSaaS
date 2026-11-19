(() => {
    const video = document.querySelector('.clarity-feature-video');
    const button = document.querySelector('.clarity-video-toggle');
    if (!video || !button) return;
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    let loaded = false;
    let wantsPlayback = !reduced.matches && !navigator.connection?.saveData;
    video.muted = true;
    video.defaultMuted = true;
    function label() {
        button.textContent = video.paused ? 'Play video' : 'Pause video';
        button.setAttribute('aria-label', video.paused ? 'Play background video' : 'Pause background video');
    }
    async function play() {
        if (!loaded) {
            const source = video.querySelector('source');
            source.src = source.dataset.src;
            video.load();
            loaded = true;
        }
        try {
            await video.play();
            if (!wantsPlayback || document.hidden) video.pause();
        } catch {
            // Keep the play control available if the browser requires a gesture.
        }
        label();
    }
    video.addEventListener('loadeddata', () => video.classList.add('is-playing'));
    video.addEventListener('play', label);
    video.addEventListener('pause', label);
    button.hidden = false;
    button.addEventListener('click', () => {
        wantsPlayback = video.paused;
        if (wantsPlayback) play();
        else video.pause();
    });
    reduced.addEventListener('change', () => {
        wantsPlayback = !reduced.matches && !navigator.connection?.saveData;
        if (!wantsPlayback) video.pause();
        else if (!document.hidden) play();
    });
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) video.pause();
        else if (wantsPlayback) play();
    });
    video.addEventListener('error', () => {
        button.textContent = 'Retry video';
        loaded = false;
    });
    if (wantsPlayback && !document.hidden) play();
    else label();
})();
