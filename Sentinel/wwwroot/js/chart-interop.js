// Utility interop helpers for Blazor Server
window.chartInterop = {
    scrollToBottom: function (element) {
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    }
};
