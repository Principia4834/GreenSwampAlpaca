// Hand Controller JS ESM Module
// Low-latency pointer event handling for mount control buttons

let dotNetRef = null;
let gridElement = null;
let activeButton = null;
const listeners = new Map();

/**
 * Initialize pointer event handlers for hand controller buttons
 * @param {object} componentRef - .NET component reference with JSInvokable methods
 * @param {HTMLElement} element - Grid element containing HC buttons
 */
export function init(componentRef, element) {
    dotNetRef = componentRef;
    gridElement = element;

    if (!gridElement) {
        console.error('Hand controller grid element not found');
        return;
    }

    // Find all directional buttons with data-hc-dir attribute
    const dirButtons = gridElement.querySelectorAll('[data-hc-dir]');
    dirButtons.forEach(btn => {
        const direction = btn.getAttribute('data-hc-dir');
        bindButton(btn, direction, false);
    });

    // Find stop button with data-hc-stop attribute
    const stopButton = gridElement.querySelector('[data-hc-stop]');
    if (stopButton) {
        bindButton(stopButton, null, true);
    }
}

/**
 * Bind pointer events to a single button
 * @param {HTMLElement} btn - Button element
 * @param {string|null} direction - Slew direction or null for stop
 * @param {boolean} isStop - True if this is the stop button
 */
function bindButton(btn, direction, isStop) {
    const handlers = {
        pointerdown: (e) => handlePointerDown(e, btn, direction, isStop),
        pointerup: (e) => handlePointerUp(e, btn, direction, isStop),
        pointerleave: (e) => handlePointerEnd(e, btn, direction, isStop),
        pointercancel: (e) => handlePointerEnd(e, btn, direction, isStop)
    };

    // Bind all events
    Object.entries(handlers).forEach(([event, handler]) => {
        btn.addEventListener(event, handler);
    });

    // Store for cleanup
    listeners.set(btn, handlers);
}

/**
 * Handle pointer down - start move or trigger stop
 */
function handlePointerDown(e, btn, direction, isStop) {
    e.preventDefault();
    e.stopPropagation();

    // Capture pointer to ensure we get up/cancel events
    btn.setPointerCapture(e.pointerId);

    if (activeButton && activeButton !== btn) {
        // Another button is active - release it first
        const prevDir = activeButton.getAttribute('data-hc-dir');
        if (prevDir && dotNetRef) {
            dotNetRef.invokeMethodAsync('OnButtonUp', prevDir);
        }
    }

    activeButton = btn;

    if (isStop) {
        // Stop button - invoke immediately
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnStopPressed');
        }
    } else if (direction && dotNetRef) {
        // Directional button - start move
        dotNetRef.invokeMethodAsync('OnButtonDown', direction);
    }
}

/**
 * Handle pointer up - stop move
 */
function handlePointerUp(e, btn, direction, isStop) {
    e.preventDefault();
    e.stopPropagation();

    if (btn.hasPointerCapture(e.pointerId)) {
        btn.releasePointerCapture(e.pointerId);
    }

    if (activeButton === btn) {
        activeButton = null;

        if (!isStop && direction && dotNetRef) {
            // Directional button released - stop move
            dotNetRef.invokeMethodAsync('OnButtonUp', direction);
        }
    }
}

/**
 * Handle pointer leave/cancel - treat as release
 */
function handlePointerEnd(e, btn, direction, isStop) {
    if (activeButton === btn) {
        handlePointerUp(e, btn, direction, isStop);
    }
}

/**
 * Cleanup - remove all event listeners
 */
export function dispose() {
    listeners.forEach((handlers, btn) => {
        Object.entries(handlers).forEach(([event, handler]) => {
            btn.removeEventListener(event, handler);
        });
    });

    listeners.clear();
    dotNetRef = null;
    gridElement = null;
    activeButton = null;
}
