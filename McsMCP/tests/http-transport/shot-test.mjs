// Verifies take_screenshot end-to-end: full capture, crop, downscale, and error handling.
// Reads back the PNG header to confirm the ACTUAL pixel dimensions, rather than trusting the JSON.
import { Client, StreamableHTTPClientTransport } from '@modelcontextprotocol/client';
import { readFileSync, existsSync, statSync, rmSync } from 'node:fs';

const t = new StreamableHTTPClientTransport(new URL('http://127.0.0.1:27016/mcp'));
const c = new Client({ name: 'shot', version: '1' }, { capabilities: {} });
await c.connect(t);

const call = async (args) => {
  const r = await c.callTool({ name: 'take_screenshot', arguments: args });
  const txt = r.content?.find(b => b.type === 'text')?.text ?? '';
  return { isError: r.isError, txt };
};

// PNG IHDR carries width/height as big-endian uint32 at byte offsets 16 and 20.
const pngSize = (p) => {
  const b = readFileSync(p);
  return { w: b.readUInt32BE(16), h: b.readUInt32BE(20), bytes: b.length, png: b.slice(1,4).toString() === 'PNG' };
};

const DIR = '/home/huang/project/MCSPlugins/output/shots';
rmSync(DIR, { recursive: true, force: true });

console.log('--- 1. full capture ---');
let r = await call({ path: `${DIR}/full.png` });
console.log(r.txt);
let s = pngSize(`${DIR}/full.png`);
console.log(`actual PNG: ${s.w}x${s.h}, ${s.bytes} bytes, valid=${s.png}`);

console.log('\n--- 2. crop (top-left 400x300 region) ---');
r = await call({ path: `${DIR}/crop.png`, cropX: 0, cropY: 0, cropWidth: 400, cropHeight: 300 });
console.log(r.txt);
s = pngSize(`${DIR}/crop.png`);
console.log(`actual PNG: ${s.w}x${s.h}  ${s.w===400&&s.h===300?'OK':'WRONG'}`);

console.log('\n--- 3. crop + downscale to width 200 ---');
r = await call({ path: `${DIR}/crop-scaled.png`, cropX: 0, cropY: 0, cropWidth: 400, cropHeight: 300, width: 200 });
console.log(r.txt);
s = pngSize(`${DIR}/crop-scaled.png`);
console.log(`actual PNG: ${s.w}x${s.h}  ${s.w===200&&s.h===150?'OK (aspect preserved)':'WRONG'}`);

console.log('\n--- 4. downscale only (width 640) ---');
r = await call({ path: `${DIR}/scaled.png`, width: 640 });
console.log(r.txt);
s = pngSize(`${DIR}/scaled.png`);
console.log(`actual PNG: ${s.w}x${s.h}`);

console.log('\n--- 5. crop offset into the frame (bottom-right quadrant) ---');
r = await call({ path: `${DIR}/quad.png`, cropX: 960, cropY: 540, cropWidth: 960, cropHeight: 540, width: 320 });
console.log(r.txt);
s = pngSize(`${DIR}/quad.png`);
console.log(`actual PNG: ${s.w}x${s.h} ${s.w===320&&s.h===180?'OK':'WRONG'}`);

console.log('\n--- error cases ---');
console.log('relative path :', (await call({ path: 'relative.png' })).txt.slice(0,150));
console.log('no path       :', (await call({})).txt.slice(0,150));
console.log('crop OOB      :', (await call({ path: `${DIR}/x.png`, cropX: 9000, cropY: 9000, cropWidth: 500, cropHeight: 500 })).txt.slice(0,180));
console.log('unwritable    :', (await call({ path: '/proc/nope/x.png' })).txt.slice(0,180));

await c.close();
