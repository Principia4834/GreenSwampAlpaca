export function downloadFile(fileName, fileData) {
    const blob = new Blob([new Uint8Array(fileData)], { type: 'application/zip' });
    const url = URL.createObjectURL(blob);

    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);

    link.click();

    document.body.removeChild(link);
    URL.revokeObjectURL(url);
}
