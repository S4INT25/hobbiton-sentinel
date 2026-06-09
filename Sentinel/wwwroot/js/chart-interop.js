// Utility interop helpers for Blazor Server
window.chartInterop = {
    scrollToBottom: function (element) {
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    },
    // Scroll so the target element is near the top of the visible area
    scrollToElement: function (element) {
        if (element) {
            element.scrollIntoView({behavior: 'smooth', block: 'start'});
        }
    }
};
