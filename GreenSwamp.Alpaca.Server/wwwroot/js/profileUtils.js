// Profile Management Utilities
// JavaScript interop for browser downloads

/**
 * Download a file to the user's browser
 * @param {string} fileName - Name of the file to download
 * @param {string} contentType - MIME type (e.g., 'application/json')
 * @param {string} base64Content - Base64-encoded file content
 */
window.downloadFile = function(fileName, contentType, base64Content) {
    try {
        // Decode base64 to binary
        const binaryString = atob(base64Content);
        const bytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
            bytes[i] = binaryString.charCodeAt(i);
        }

        // Create blob
        const blob = new Blob([bytes], { type: contentType });

        // Create download link
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        
        // Trigger download
        document.body.appendChild(link);
        link.click();
        
        // Cleanup
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
        
        console.log(`File '${fileName}' downloaded successfully`);
    } catch (error) {
        console.error('Failed to download file:', error);
        throw error;
    }
};
