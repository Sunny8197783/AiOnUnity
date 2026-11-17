const stub = new URL('./endure-three-stub.mjs', import.meta.url).href;
const compose = new URL('./endure-compose-stub.mjs', import.meta.url).href;
const notecheck = new URL('./endure-notecheck-stub.mjs', import.meta.url).href;
export async function resolve(spec, ctx, next) {
  if (spec === 'three' || spec.startsWith('three/')) return { url: stub, shortCircuit: true };
  if (spec.endsWith('/ui/compose.js') && ctx.parentURL?.includes('/story/')) return { url: compose, shortCircuit: true };
  if (spec.endsWith('/notecheck.js') && ctx.parentURL?.includes('/story/')) return { url: notecheck, shortCircuit: true };
  return next(spec, ctx);
}
