const initializationAttemptedKey = "gsAlpacaCultureInitializationAttempted";

export function hasCulturePreference(cookieName) {
    const hasCookie = document.cookie
        .split(";")
        .some(cookie => cookie.trim().startsWith(`${cookieName}=`));

    if (hasCookie) {
        sessionStorage.removeItem(initializationAttemptedKey);
        return true;
    }

    return sessionStorage.getItem(initializationAttemptedKey) === "true";
}

export function getCulturePreference(cookieName) {
    const cookie = document.cookie
        .split(";")
        .map(value => value.trim())
        .find(value => value.startsWith(`${cookieName}=`));

    if (!cookie) {
        return null;
    }

    const value = decodeURIComponent(cookie.slice(cookie.indexOf("=") + 1));
    const match = /^c=([^|]+)\|uic=(.+)$/.exec(value);
    return match ? [match[1], match[2]] : null;
}

export function markInitializationAttempted() {
    sessionStorage.setItem(initializationAttemptedKey, "true");
}