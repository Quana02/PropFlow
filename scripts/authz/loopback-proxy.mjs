import { createServer, request } from 'node:https';
import { readFileSync } from 'node:fs';

const certificate = readFileSync(process.env.PROPFLOW_AUTHZ_CERT);
const options = {
  pfx: readFileSync(process.env.PROPFLOW_AUTHZ_PFX),
  passphrase: process.env.PROPFLOW_AUTHZ_PFX_PASSWORD
};
const hopHeaders = new Set(['connection', 'keep-alive', 'proxy-authenticate', 'proxy-authorization',
  'te', 'trailer', 'transfer-encoding', 'upgrade']);
const reconnectFixture = process.env.PROPFLOW_NOTIFICATION_GROUP === 'Reconnect';
const fixtureClients = new Map();

function upstream(incoming, upgrade = false) {
  const path = incoming.url;
  if (!path.startsWith('/') || path.startsWith('//') || path.includes('\\')) return undefined;
  const pathname = new URL(path, 'https://localhost').pathname;
  const port = /^\/(api\/v1|hubs)(\/|$)/i.test(pathname) ? 47101 : 47201;
  const connectionHeaders = String(incoming.headers.connection ?? '').toLowerCase().split(',').map(value => value.trim());
  const headers = Object.fromEntries(Object.entries(incoming.headers).filter(([name]) =>
    !hopHeaders.has(name) && !connectionHeaders.includes(name) && !name.startsWith('x-forwarded-') && name !== 'forwarded'));
  headers['x-forwarded-for'] = incoming.socket.remoteAddress;
  headers['x-forwarded-proto'] = 'https';
  if (upgrade) {
    headers.connection = 'Upgrade';
    headers.upgrade = 'websocket';
  }
  return request({ host: '127.0.0.1', servername: 'localhost', port, path, method: incoming.method,
    headers, ca: certificate, timeout: 120000 });
}

const server = createServer(options, (incoming, response) => {
  if (reconnectFixture && incoming.method === 'DELETE') {
    const id = /^\/__propflow_fixture\/socket\/([a-f0-9-]{36})$/.exec(incoming.url)?.[1];
    if (id) {
      const sockets = fixtureClients.get(id);
      if (!sockets?.size) return response.writeHead(404).end();
      for (const socket of sockets) socket.destroy();
      return response.writeHead(204).end();
    }
  }
  const outgoing = upstream(incoming);
  if (!outgoing) return response.writeHead(400).end();
  outgoing.once('response', message => {
    const headers = Object.fromEntries(Object.entries(message.headers).filter(([name]) => !hopHeaders.has(name)));
    response.writeHead(message.statusCode, headers);
    message.pipe(response);
  });
  outgoing.once('timeout', () => outgoing.destroy());
  outgoing.once('error', () => {
    if (response.headersSent) response.destroy();
    else response.writeHead(502).end();
  });
  response.once('close', () => outgoing.destroy());
  incoming.pipe(outgoing);
});

server.on('upgrade', (incoming, client, clientHead) => {
  const outgoing = upstream(incoming, true);
  if (!outgoing || incoming.headers.upgrade?.toLowerCase() !== 'websocket')
    return client.end('HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n');
  outgoing.once('upgrade', (message, remote, remoteHead) => {
    if (reconnectFixture) {
      const id = new URL(incoming.url, 'https://localhost').searchParams.get('propflow_fixture_client');
      if (id && /^[a-f0-9-]{36}$/.test(id)) {
        if (!fixtureClients.has(id)) fixtureClients.set(id, new Set());
        const sockets = fixtureClients.get(id);
        sockets.add(client);
        client.once('close', () => {
          sockets.delete(client);
          if (sockets.size === 0) fixtureClients.delete(id);
        });
      }
    }
    let response = 'HTTP/1.1 101 Switching Protocols\r\n';
    for (let index = 0; index < message.rawHeaders.length; index += 2)
      response += message.rawHeaders[index] + ': ' + message.rawHeaders[index + 1] + '\r\n';
    client.write(response + '\r\n');
    if (remoteHead.length) client.write(remoteHead);
    if (clientHead.length) remote.write(clientHead);
    remote.on('error', () => client.destroy());
    client.on('error', () => remote.destroy());
    remote.once('close', () => client.destroy());
    client.once('close', () => remote.destroy());
    remote.pipe(client);
    client.pipe(remote);
  });
  outgoing.once('response', message => {
    message.resume();
    client.end(`HTTP/1.1 ${message.statusCode} Upstream response\r\nConnection: close\r\nContent-Length: 0\r\n\r\n`);
  });
  outgoing.once('timeout', () => outgoing.destroy());
  outgoing.once('error', () => client.destroy());
  client.once('close', () => outgoing.destroy());
  outgoing.end();
});
server.listen(47301, '127.0.0.1', () => console.log('Proxy HTTPS loopback 47301'));
