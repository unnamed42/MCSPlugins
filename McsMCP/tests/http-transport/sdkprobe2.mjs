import { Client, StreamableHTTPClientTransport } from '@modelcontextprotocol/client';
const t = new StreamableHTTPClientTransport(new URL(process.argv[2] ?? 'http://127.0.0.1:27995/mcp'));
const c = new Client({ name: 'p2', version: '1' }, { capabilities: {} });
await c.connect(t);
// Arguments must survive the round trip, including non-ASCII.
const r = await c.callTool({ name: 'echo_args', arguments: { text: '你好 McsMCP — round trip ✓' } });
console.log('[SDK] echo ->', r.content?.[0]?.text);
// tools/list must expose the declared schema.
const tools = await c.listTools();
const echo = tools.tools.find(x => x.name === 'echo_args');
console.log('[SDK] echo_args inputSchema =', JSON.stringify(echo.inputSchema));
await c.close();
console.log('[SDK] ok');
