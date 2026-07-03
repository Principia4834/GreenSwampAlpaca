window.floatingWindowInterop = {
    init: (dotNetRef, elementId, headerId, resizeId) => {
        const win = document.getElementById(elementId);
        const header = document.getElementById(headerId);
        const resizer = document.getElementById(resizeId);
        if (!win) return;

        const clamp = (value, min) => Math.max(min, value);

        if (header) {
            header.onmousedown = (e) => {
                e.preventDefault();

                let startX = e.clientX;
                let startY = e.clientY;

                const onMouseMove = (moveEvent) => {
                    const deltaX = moveEvent.clientX - startX;
                    const deltaY = moveEvent.clientY - startY;
                    startX = moveEvent.clientX;
                    startY = moveEvent.clientY;

                    const newX = win.offsetLeft + deltaX;
                    const newY = win.offsetTop + deltaY;

                    win.style.left = `${newX}px`;
                    win.style.top = `${newY}px`;
                    dotNetRef.invokeMethodAsync("SavePosition", newX, newY);
                };

                const onMouseUp = () => {
                    document.removeEventListener("mousemove", onMouseMove);
                    document.removeEventListener("mouseup", onMouseUp);
                };

                document.addEventListener("mousemove", onMouseMove);
                document.addEventListener("mouseup", onMouseUp);
            };
        }

        if (resizer) {
            resizer.onmousedown = (e) => {
                e.preventDefault();

                const startW = win.offsetWidth;
                const startH = win.offsetHeight;
                const startX = e.clientX;
                const startY = e.clientY;

                const onMouseMove = (moveEvent) => {
                    const newW = clamp(startW + (moveEvent.clientX - startX), 200);
                    const newH = clamp(startH + (moveEvent.clientY - startY), 150);

                    win.style.width = `${newW}px`;
                    win.style.height = `${newH}px`;
                    dotNetRef.invokeMethodAsync("SaveSize", newW, newH);
                };

                const onMouseUp = () => {
                    document.removeEventListener("mousemove", onMouseMove);
                    document.removeEventListener("mouseup", onMouseUp);
                };

                document.addEventListener("mousemove", onMouseMove);
                document.addEventListener("mouseup", onMouseUp);
            };
        }
    }
};