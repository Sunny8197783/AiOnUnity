// Stand-in for notecheck.js createReread: records the 3D calls.
export function createReread(world, { start, reduceMotion = false }) {
  const log = (globalThis.__relay ||= { compose: [], captions: [], answers: [], notes: [] }).fx = [];
  log.push(['start', start.x, start.z]);
  return {
    eye: (p) => log.push(['eye', p]), crane: () => log.push(['crane']), showPath: (t) => log.push(['path', t.length]),
    pin: (label, at, was, now, scale) => log.push(['pin', String(label), at.x, at.z, was, now, scale ?? null]),
    link: (a, b) => log.push(['link', a.x, a.z, b.x, b.z]), dispose: () => log.push(['dispose']),
  };
}
