window.goToPanelSmartInput = {
    formatAndPreserveCaret: function (elementId, formattedValue, oldValue) {
        const input = document.getElementById(elementId);
        if (!input || typeof formattedValue !== "string") {
            return;
        }

        const selectionStart = input.selectionStart ?? oldValue?.length ?? 0;
        const selectionEnd = input.selectionEnd ?? selectionStart;
        const delta = formattedValue.length - (oldValue || "").length;

        input.value = formattedValue;

        const newStart = Math.max(0, selectionStart + delta);
        const newEnd = Math.max(newStart, selectionEnd + delta);
        input.setSelectionRange(newStart, newEnd);
    }
};
