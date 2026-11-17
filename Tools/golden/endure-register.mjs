// Node stand-ins for the endure/seoyeon goldens: stubs three, ui/compose.js and notecheck.js (endure-loader.mjs) and a bare document.
// Use: node --import ./endure-register.mjs make-golden-story-endure.mjs <dist> <level dir> <out>
import { register } from 'node:module';
register('./endure-loader.mjs', import.meta.url);
globalThis.document = { getElementById: () => null, querySelector: () => null, createElement: () => ({ style: {}, classList: { add() {}, remove() {} }, append() {}, setAttribute() {}, addEventListener() {} }), addEventListener() {}, removeEventListener() {} };
globalThis.window = globalThis; globalThis.addEventListener = () => {}; globalThis.removeEventListener = () => {};
globalThis.localStorage = { getItem: () => null, setItem() {} };
