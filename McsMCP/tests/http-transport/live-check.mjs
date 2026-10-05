#!/usr/bin/env node
// End-to-end check against a RUNNING GAME (not the harness), using the same
// @modelcontextprotocol/client SDK that dsh-mcp-client uses.
//
//   node live-check.mjs [http://127.0.0.1:27016/mcp]
//
// Read-only by design: every set_config call uses save:false and restores the previous value, so
// running this against a live game leaves no trace in any .cfg file.
import { Client, StreamableHTTPClientTransport } from '@modelcontextprotocol/client';
import { readFileSync, existsSync, rmSync } from 'node:fs';

const url = process.argv[2] ?? 'http://127.0.0.1:27016/mcp';
const t = new StreamableHTTPClientTransport(new URL(url));
const c = new Client({ name: 'mcs-live-check', version: '1.0.0' }, { capabilities: {} });

let pass = 0, fail = 0;
const ok = (cond, label, detail = '') => {
  console.log(`${cond ? '  ok  ' : ' FAIL '} ${label}${detail ? '  ' + detail : ''}`);
  cond ? pass++ : fail++;
};

await c.connect(t);
console.log(`connected to ${url} (protocolVersion=${t.protocolVersion ?? 'n/a'})\n`);

const text = async (name, args = {}) => {
  const r = await c.callTool({ name, arguments: args });
  return { isError: r.isError, text: r.content?.find(b => b.type === 'text')?.text ?? '', raw: r };
};

// --- discovery ---------------------------------------------------------------
console.log('discovery');
const { tools } = await c.listTools();
ok(tools.length > 40, `tools/list advertises ${tools.length} tools`);
ok(tools.every(x => x.inputSchema && typeof x.inputSchema === 'object'),
   'every tool carries an inputSchema object (SDK rejects the whole list otherwise)');

// --- diagnostics -------------------------------------------------------------
console.log('\ndiagnostics');
const mt = await text('main_thread_status');
ok(mt.text.includes('"verdict":"running"'), 'main_thread_status reports running');

const gi = await text('get_game_info');
ok(gi.text.includes('觅长生'), 'get_game_info names the game');
ok(gi.text.includes('bepInExVersion'), 'get_game_info reports the BepInEx version');

const ti = await text('get_time_info');
ok(!ti.isError && ti.text.includes('timeScale'), 'get_time_info (main-thread tool) responds');

// --- log capture -------------------------------------------------------------
console.log('\nlog capture');
const rl = await text('read_logs', { count: 5 });
ok(!rl.isError && rl.text.length > 0, 'read_logs returns buffered lines');

// --- Unity inspection (the tools IL2CPP had to disable) ----------------------
console.log('\nUnity inspection');
const fo = await text('find_objects_of_type', { typeName: 'Camera', limit: 3 });
ok(fo.text.includes('UnityEngine.Camera'), 'find_objects_of_type finds Cameras');

const lgo = await text('list_game_objects', { depth: 1 });
// The decisive check for this port: under IL2CPP every component reported as "Component".
ok(lgo.text.includes('Transform') && !/"components":\["Component"/.test(lgo.text),
   'components report CONCRETE type names (no IL2CPP proxy collapse)');

const ic = await text('inspect_component', { gameObjectPath: 'NewMain(Clone)/Camera', componentType: 'Camera' });
ok(ic.text.includes('fieldOfView'), 'inspect_component reads real component properties');

const iuo = await text('inspect_unity_object', { typeName: 'Camera', fields: ['name', 'enabled'], count: 2 });
ok(iuo.text.includes('"instances"'), 'inspect_unity_object reads named fields');

// --- code execution ----------------------------------------------------------
console.log('\ncode execution');
const ec = await text('execute_csharp', { code: 'UnityEngine.Application.unityVersion' });
ok(!ec.isError && /20\d\d\./.test(ec.text), 'execute_csharp evaluates game types (embedded Mono.CSharp works)');

const ee = await text('evaluate_expression', { expression: 'UnityEngine.Time.frameCount' });
ok(!ee.isError && /^\d+/.test(ee.text.trim()), 'evaluate_expression works');

// --- patching ----------------------------------------------------------------
console.log('\npatching');
const lp = await text('list_patches', { owner: 'MCSPlugins' });
ok(lp.text.includes('set_runInBackground'), 'list_patches sees our own runInBackground patch');

// --- config ------------------------------------------------------------------
console.log('\nconfig');
const M = 'MCSPlugins.McsMCP';
const lc = await text('list_configs');
ok(lc.text.includes('categoryCount'), 'list_configs enumerates config files');

const gc = await text('get_config', { mod: M, key: 'Server.Port' });
const before = JSON.parse(gc.text);
ok(before.value === '27015', `get_config reads our own Server.Port (${before.value})`);

// Non-destructive write check, then restore the original value.
const sc = await text('set_config', { mod: M, key: 'Server.Port', value: 27016, save: false, confirm: M + '.Server.Port' });
ok(sc.text.includes('"changed":true'), 'set_config writes through the live ConfigFile');
await text('set_config', { mod: M, key: 'Server.Port', value: Number(before.value), save: false, confirm: M + '.Server.Port' });
const after = JSON.parse((await text('get_config', { mod: M, key: 'Server.Port' })).text);
ok(after.value === before.value, `set_config change was restored (${after.value})`);

// --- screenshot --------------------------------------------------------------
// take_screenshot WRITES A FILE and returns a short JSON description - it must NOT return the image
// inline (a full frame is ~9 MB / ~3M tokens).
//
// ★ TWO things make this check harder than it looks, and both bit us once:
//
// 1. THE GAME'S FILESYSTEM IS NOT OURS. The game runs outside this sandbox, so a path we can read is
//    not necessarily one it can write and vice versa. Verify through the game: evaluate_expression
//    runs INSIDE it, so File.Exists / ReadAllBytes there is ground truth.
//
// 2. A WINE PATH LETTER DOES NOT MEAN WHAT IT LOOKS LIKE. The game runs under Proton, where
//    S: is the Steam library and Z: is the Linux root. Our repo lives at /home/huang/..., so it is
//    Z:\\\\home\\\\huang\\\\... - NOT S:. Writing to S:\\\\home\\\\... does not fail: Windows creates the
//    directories, so you get a REAL file in a WRONG place (/run/media/.../SteamLibrary/home/...).
//    This is why the assertion below checks the HOST path too - a File.Exists probe cannot catch a
//    mis-mapped drive, because the game is only confirming the file it just created.
console.log('\nscreenshot');
const hostDir = process.env.MCS_SHOT_DIR ?? '/home/huang/project/MCSPlugins/output';
// Wine/Proton: Z: is the Linux root; S: is the Steam library. Derive the game-side path from the
// host path so the two can never drift apart.
const shotPath = process.env.MCS_SHOT_PATH ?? 'Z:' + hostDir + '/_shot_check.png';
const hostPath = hostDir + '/_shot_check.png';

const ss = await text('take_screenshot', { path: shotPath });
const meta = ss.isError ? null : JSON.parse(ss.text);
ok(ss.raw.content?.every(b => b.type !== 'image'),
   'take_screenshot does not return the image inline');
ok(!!meta && meta.outputWidth > 0 && meta.bytes > 0,
   `take_screenshot reports a written file (${meta?.outputWidth}x${meta?.outputHeight}, ${meta?.bytes} bytes)`);

// (a) the game confirms it wrote the file
const evGame = async (expr) => {
  const r = await c.callTool({ name: 'evaluate_expression', arguments: { expression: expr } });
  return r.isError ? null : (r.content.find(b => b.type === 'text')?.text ?? '').trim();
};
// shotPath already uses single backslashes, which is what C#'s verbatim string (@"...") wants.
// Do NOT escape it again: doubled separators survive ReadAllBytes (Windows tolerates them) but make
// File.Exists return False, which produced a false failure here once.
const P = `@"${shotPath}"`;
ok((await evGame(`System.IO.File.Exists(${P})`))?.toLowerCase() === 'true',
   'the game confirms the file exists at the requested path');

// (b) ★ and it landed where WE asked, on the host side. This is the assertion that catches a
// mis-mapped drive letter; (a) alone would pass even if the file went somewhere else entirely.
if (existsSync(hostPath)) {
  const buf = readFileSync(hostPath);
  const w = buf.readUInt32BE(16), h = buf.readUInt32BE(20);
  ok(buf.subarray(1, 4).toString() === 'PNG' && w === meta?.outputWidth && h === meta?.outputHeight,
     `file is on the HOST at the requested path, and is a real PNG (${w}x${h})`);
  rmSync(hostPath, { force: true });
} else {
  ok(false, `file reached the requested HOST path ${hostPath} `
          + `(if this fails, check the Wine drive letter: Z: is the Linux root, S: is the Steam library)`);
}

await c.close();
console.log(`\n${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
