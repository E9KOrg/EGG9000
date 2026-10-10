// Please see documentation at https://docs.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

function copyIdToClipboard(id, notifId) {
    const content = (typeof id === "object") ? id.innerText : id.toString();
    navigator.clipboard.writeText(content);

    if (notifId) {
        var notification = document.getElementById(notifId);
        notification.style.display = "block";
        notification.style.opacity = "1";

        setTimeout(function () {
            notification.style.opacity = "0";
        }, 2000);
    }
}

// Current ApexCharts theme mode from the Bootstrap dark-mode attribute.
function chartThemeMode() {
    return document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'dark' : 'light';
}

// POST a JSON body and return the parsed response. Callers own their own success/error handling.
async function postJson(url, body) {
    const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body)
    });
    return await response.json();
}
