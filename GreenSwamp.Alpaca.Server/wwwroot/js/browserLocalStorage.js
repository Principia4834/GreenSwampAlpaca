function operationResult(operation) {
    try {
        operation();
        return { available: true, error: null };
    } catch (error) {
        return { available: false, error: error?.name || 'StorageError' };
    }
}

export function getItem(key) {
    try {
        return { available: true, value: window.localStorage.getItem(key), error: null };
    } catch (error) {
        return { available: false, value: null, error: error?.name || 'StorageError' };
    }
}

export function setItem(key, value) {
    return operationResult(() => window.localStorage.setItem(key, value));
}

export function removeItem(key) {
    return operationResult(() => window.localStorage.removeItem(key));
}