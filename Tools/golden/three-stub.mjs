// node --import ./three-stub.mjs ...: resolves 'three' and 'three/addons/*' to inert stand-ins (Proxies that can be
// called, constructed and read without effect), so story modules that import three.js load under node without a
// browser. The names exported are the ones the given dist uses (THREE.X and named addon imports), read at start-up.
import { register } from 'node:module';
register(new URL('./three-stub-hooks.mjs', import.meta.url).href, import.meta.url);
