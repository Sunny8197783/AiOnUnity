// Scripted stand-in for ui/compose.js: compose() and rereadCaption() answer from globalThis.__relay, the rest records.
const R = () => (globalThis.__relay ||= { compose: [], captions: [], answers: [], notes: [] });
export function compose(opts) { R().opened = opts; return Promise.resolve(R().compose.shift()); }
export function showNote(hand, mode) { R().notes.push(mode); }
export function hideNote() { R().notes.push('hide'); }
export function rereadCaption(text, pin, last = false) { R().captions.push([text, pin ? [pin.factor, pin.anchor, pin.was, pin.now] : null, last]); return Promise.resolve(R().answers.shift() ?? 'next'); }
