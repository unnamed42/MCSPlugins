// Regression check for the read_logs level+filter bug (docs/mcp-streamable-http.md §11.2).
// BEFORE the fix, 'level=warning' + 'filter=<text>' ALWAYS returned 0 lines, because the
// server built a regex-looking string and GetLogs matched it literally. A healthy result here
// is a non-zero count that is a SUBSET of the count for level alone.
import { Client, StreamableHTTPClientTransport } from '@modelcontextprotocol/client';
const t = new StreamableHTTPClientTransport(new URL('http://127.0.0.1:27016/mcp'));
const c = new Client({ name: 'lvl', version: '1' }, { capabilities: {} });
await c.connect(t);
const call = async (a) => {
  const r = await c.callTool({ name: 'read_logs', arguments: a });
  const txt = r.content?.find(b => b.type === 'text')?.text ?? '';
  const n = txt.startsWith('No log entries') ? 0 : txt.split('\n').filter(l=>l.trim()).length;
  console.log(JSON.stringify(a).padEnd(52), '->', n, 'lines');
};
await call({ count: 1000 });
await call({ count: 1000, level: 'error' });
await call({ count: 1000, level: 'error', filter: '加载' });   // <-- suspected bug
await call({ count: 1000, level: 'warning' });
await call({ count: 1000, level: 'warning', filter: 'Lua' });  // <-- suspected bug
await call({ count: 1000, filter: 'Lua' });
await c.close();
