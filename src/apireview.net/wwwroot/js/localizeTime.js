function localizeTime() {
    // Update <time> elements to user's local time,
    // but if the date would differ keep Pacific time and show PST/PDT
    document.querySelectorAll('time[datetime]').forEach(function (el) {
        var isoStr = el.getAttribute('datetime');
        var dt = new Date(isoStr);
        var cell = el.closest('[data-date]');
        var cellDate = cell ? cell.getAttribute('data-date') : null;
        var localDate = dt.getFullYear() + '-' +
            String(dt.getMonth() + 1).padStart(2, '0') + '-' +
            String(dt.getDate()).padStart(2, '0');
        if (cellDate && localDate !== cellDate) {
            // Date would change — keep Pacific time; label it PST or PDT from the offset
            var offsetMatch = isoStr.match(/([+-]\d{2}):\d{2}$/);
            var tzLabel = (offsetMatch && offsetMatch[1] === '-07') ? 'PDT' : 'PST';
            el.textContent = el.textContent + '\u00a0' + tzLabel;
        } else {
            el.textContent = dt.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
        }
    });

    // Re-highlight today/past cells using the user's local date
    var now = new Date();
    var todayStr = now.getFullYear() + '-' +
        String(now.getMonth() + 1).padStart(2, '0') + '-' +
        String(now.getDate()).padStart(2, '0');
    document.querySelectorAll('[data-date]').forEach(function (el) {
        var d = el.getAttribute('data-date');
        el.classList.toggle('today', d === todayStr);
        el.classList.toggle('past', d < todayStr);
    });
}

const observeUrlChange = () => {
    if (document.location.href.includes("/schedule")) {
        localizeTime();
    }
    let oldHref = document.location.href;
    const body = document.querySelector("body");
    const observer = new MutationObserver(mutations => {
        if (oldHref !== document.location.href) {
            oldHref = document.location.href;
            if (document.location.href.includes("/schedule")) {
                localizeTime();
            }
        }
    });
    observer.observe(body, { childList: true, subtree: true });
};
window.onload = observeUrlChange;