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

export function markInitializationAttempted() {
    sessionStorage.setItem(initializationAttemptedKey, "true");
}