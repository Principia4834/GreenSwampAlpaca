/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

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
    },

    // Triggers a browser download for a data-URI (e.g. PNG from ApexCharts.GetDataUriAsync).
    downloadDataUri: (dataUri, filename) => {
        const link = document.createElement('a');
        link.href = dataUri;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    },

    // Calls the ApexCharts built-in CSV export on the first chart instance on the page.
    exportChartCsv: (filename) => {
        const instances = window.Apex && window.Apex._chartInstances;
        if (instances && instances.length > 0) {
            instances[0].exports.exportToCSV({ fileName: filename });
        }
    }
};