// chartWindowInterop.js
// Opens GS chart pages in separate browser windows and notifies the Blazor
// circuit when a popup is blocked or a child window closes unexpectedly.

window.gsChartWindows = window.gsChartWindows || {};

window.chartWindowInterop = {

    // Opens a chart page in a new browser window.
    // dotNetRef: DotNetObjectReference to invoke callbacks on the Blazor component.
    // url:       The Blazor page URL (e.g. "/charts/radec").
    // windowKey: A unique string so we can track and focus-reuse the same window.
    // width, height: Desired window dimensions in pixels.
    open: (dotNetRef, url, windowKey, width, height) => {
        // If the window for this key is already open, just bring it to the front.
        const existing = window.gsChartWindows[windowKey];
        if (existing && !existing.closed) {
            existing.focus();
            return;
        }

        const features = [
            `width=${width}`,
            `height=${height}`,
            'resizable=yes',
            'scrollbars=yes',
            'toolbar=no',
            'menubar=no',
            'location=no',
            'status=no'
        ].join(',');

        const win = window.open(url, windowKey, features);

        if (!win || win.closed || typeof win.closed === 'undefined') {
            // Popup was blocked — notify the Blazor component to show a snackbar.
            dotNetRef.invokeMethodAsync('OnPopupBlocked', url);
            return;
        }

        window.gsChartWindows[windowKey] = win;

        // Poll until the child window closes and notify the Blazor component.
        const pollInterval = setInterval(() => {
            if (win.closed) {
                clearInterval(pollInterval);
                delete window.gsChartWindows[windowKey];
                dotNetRef.invokeMethodAsync('OnChartWindowClosed', windowKey);
            }
        }, 1000);
    },

    // Returns true if the named window is currently open.
    isOpen: (windowKey) => {
        const win = window.gsChartWindows[windowKey];
        return !!(win && !win.closed);
    },

    // Closes the named window programmatically (e.g. server shutdown).
    close: (windowKey) => {
        const win = window.gsChartWindows[windowKey];
        if (win && !win.closed) win.close();
        delete window.gsChartWindows[windowKey];
    }
};
