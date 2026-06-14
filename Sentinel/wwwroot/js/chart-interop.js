// Utility interop helpers for Blazor Server
window.chartInterop = {
    scrollToBottom: function (element) {
        if (element) {
            element.scrollTop = element.scrollHeight;
        }
    },
    scrollToElement: function (element) {
        if (element) {
            element.scrollIntoView({behavior: 'smooth', block: 'start'});
        }
    },
    downloadChartById: function (containerId, filename) {
        var containerEl = document.getElementById(containerId);
        return this.downloadChartPng(containerEl, filename);
    },
    downloadChartPng: function (containerEl, filename) {
        if (!containerEl) return false;
        var svg = containerEl.querySelector('.apexcharts-svg');
        if (!svg) return false;

        var clone = svg.cloneNode(true);
        clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
        // Set a dark background on the clone
        var rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        rect.setAttribute('width', '100%');
        rect.setAttribute('height', '100%');
        rect.setAttribute('fill', '#0a0a0f');
        clone.insertBefore(rect, clone.firstChild);

        var svgData = new XMLSerializer().serializeToString(clone);
        var canvas = document.createElement('canvas');
        var svgSize = svg.getBoundingClientRect();
        canvas.width = svgSize.width * 2;
        canvas.height = svgSize.height * 2;
        var ctx = canvas.getContext('2d');
        ctx.scale(2, 2);
        var img = new Image();
        img.onload = function () {
            ctx.drawImage(img, 0, 0);
            var a = document.createElement('a');
            a.download = (filename || 'chart') + '.png';
            a.href = canvas.toDataURL('image/png');
            a.click();
        };
        img.src = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svgData)));
        return true;
    },
    copyToClipboard: function (text) {
        return navigator.clipboard.writeText(text).then(function () { return true; }, function () { return false; });
    },
    getChartPngBase64: async function (containerEl) {
        if (!containerEl) return null;
        var svg = containerEl.querySelector('.apexcharts-svg');
        if (!svg) return null;

        var clone = svg.cloneNode(true);
        clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
        var rect = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
        rect.setAttribute('width', '100%');
        rect.setAttribute('height', '100%');
        rect.setAttribute('fill', '#0a0a0f');
        clone.insertBefore(rect, clone.firstChild);

        var svgData = new XMLSerializer().serializeToString(clone);
        var canvas = document.createElement('canvas');
        var svgSize = svg.getBoundingClientRect();
        canvas.width = svgSize.width * 2;
        canvas.height = svgSize.height * 2;
        var ctx = canvas.getContext('2d');
        ctx.scale(2, 2);

        return new Promise(function (resolve) {
            var img = new Image();
            img.onload = function () {
                ctx.drawImage(img, 0, 0);
                resolve(canvas.toDataURL('image/png'));
            };
            img.onerror = function () { resolve(null); };
            img.src = 'data:image/svg+xml;base64,' + btoa(unescape(encodeURIComponent(svgData)));
        });
    }
};
