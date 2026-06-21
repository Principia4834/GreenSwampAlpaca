export function speak(text, rate = 0.8, volume = 1) {
        if (!window.speechSynthesis) return;
    window.speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(text);
    utterance.rate = rate;
    utterance.volume = volume;
    window.speechSynthesis.speak(utterance);
    }

    export function stop() {
        if (window.speechSynthesis) window.speechSynthesis.cancel();
}