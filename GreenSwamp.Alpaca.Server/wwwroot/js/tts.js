// Speak text using the Web Speech API.
// voiceName: exact voice name string, or "" to use the browser default.
// volumePct: integer 0-100; converted to 0.0-1.0 for the API.
// rate:      speech rate (0.1-10, default 0.8).
export function speak(text, voiceName = "", volumePct = 100, rate = 0.8) {
    if (!window.speechSynthesis) return;
    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.rate = rate;
    utterance.volume = Math.max(0, Math.min(100, volumePct)) / 100;
    if (voiceName) {
        const voices = window.speechSynthesis.getVoices();
        const match = voices.find(v => v.name === voiceName);
        if (match) utterance.voice = match;
    }
    window.speechSynthesis.speak(utterance);
}

export function stop() {
    if (window.speechSynthesis) window.speechSynthesis.cancel();
}

// Returns a JSON array of available voice objects filtered by the given BCP-47 language tag.
// Pass "" to return all voices.
export function getVoices(languageTag = "") {
    if (!window.speechSynthesis) return "[]";
    const voices = window.speechSynthesis.getVoices();
    const filtered = languageTag
        ? voices.filter(v => v.lang.toLowerCase().startsWith(languageTag.toLowerCase()))
        : voices;
    return JSON.stringify(filtered.map(v => ({ name: v.name, lang: v.lang, default: v.default })));
}

// Returns the browser's preferred BCP-47 language tag (e.g. "en-US").
export function getLanguage() {
    return window.navigator?.language ?? "";
}