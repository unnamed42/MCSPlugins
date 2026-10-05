// The decisive test: connect with the ACTUAL @modelcontextprotocol/client SDK that
// dsh-mcp-client uses (StreamableHTTPClientTransport), doing a real handshake + tools/list.
import { Client } from '@modelcontextprotocol/client';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/client';

const url = new URL(process.argv[2] ?? 'http://127.0.0.1:27999/mcp');
const transport = new StreamableHTTPClientTransport(url);
const client = new Client({ name: 'dsh-probe', version: '1.0.0' }, { capabilities: {} });

try {
  await client.connect(transport);
  console.log('[SDK] connected. negotiated protocolVersion =', transport.protocolVersion ?? '(n/a)');
  const tools = await client.listTools();
  console.log('[SDK] tools/list ->', tools.tools.map(t => t.name).join(', '));
  const res = await client.callTool({ name: 'probe_tool', arguments: {} });
  console.log('[SDK] tools/call ->', JSON.stringify(res.content));
  await client.close();
  console.log('[SDK] closed cleanly');
} catch (e) {
  console.error('[SDK] FAILED:', e?.message ?? e);
  process.exit(1);
}
