"use strict";
function updateElapsed() {
    document.querySelectorAll("time[data-start]").forEach(element => {
        const seconds = Math.max(0, Math.floor((Date.now() - Number(element.dataset.start)) / 1000));
        element.textContent = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
    });
}
updateElapsed();
setInterval(updateElapsed, 1000);
