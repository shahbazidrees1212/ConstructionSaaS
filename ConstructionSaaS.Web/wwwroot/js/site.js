(() => {
    const toggle = document.querySelector('.password-toggle');
    const password = document.getElementById('Password');
    if (toggle && password) {
        toggle.addEventListener('click', () => {
            const show = password.type === 'password';
            password.type = show ? 'text' : 'password';
            toggle.textContent = show ? 'Hide' : 'Show';
            toggle.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
            toggle.setAttribute('aria-pressed', String(show));
        });
    }
    const video = document.getElementById('construction-video');
    const control = document.querySelector('.video-toggle');
    if (!video || !control) return;
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const sync = () => { control.textContent = video.paused ? 'Play video' : 'Pause video'; };
    const play = () => { video.play().then(sync).catch(sync); };
    control.hidden = false;
    control.addEventListener('click', () => { if (video.paused) play(); else video.pause(); });
    video.addEventListener('play', sync);
    video.addEventListener('pause', sync);
    video.addEventListener('error', () => { control.hidden = true; });
    const preferences = () => {
        control.hidden = motion.matches;
        if (motion.matches) video.pause();
        else if (!navigator.connection?.saveData) play();
        sync();
    };
    motion.addEventListener('change', preferences);
    preferences();
})();
