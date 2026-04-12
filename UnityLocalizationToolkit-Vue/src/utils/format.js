export function formatBytes(bytes) {
    if (bytes >= 1024 * 1024 * 1024)
        return (bytes / (1024 * 1024 * 1024)).toFixed(1) + ' GB';
    if (bytes >= 1024 * 1024)
        return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    if (bytes >= 1024)
        return (bytes / 1024).toFixed(1) + ' KB';
    return `${bytes} B`;
}
export function formatSpeed(bytesPerSec) {
    if (bytesPerSec >= 1024 * 1024)
        return (bytesPerSec / (1024 * 1024)).toFixed(1) + ' MB/s';
    return (bytesPerSec / 1024).toFixed(0) + ' KB/s';
}
