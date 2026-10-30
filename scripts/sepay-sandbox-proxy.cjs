// Expose only the Sandbox webhook to the temporary demo tunnel, not the rest of the API.
const http = require('node:http');
const https = require('node:https');
const webhookPath = '/api/v1/payments/webhooks/sepay/sandbox';
http.createServer((request, response) => {
  if (request.url !== webhookPath || request.method !== 'POST') {
    response.writeHead(404).end(); request.resume(); return;
  }
  const chunks = []; let size = 0;
  request.on('data', chunk => {
    size += chunk.length;
    if (size > 16384) { response.writeHead(413).end(); request.destroy(); }
    else chunks.push(chunk);
  });
  request.on('end', () => {
    if (size > 16384) return;
    const body = Buffer.concat(chunks);
    const upstream = https.request({ hostname: 'localhost', port: 7101, path: webhookPath, method: 'POST',
      rejectUnauthorized: false, timeout: 20000,
      headers: { 'content-type': 'application/json', 'content-length': body.length,
        authorization: request.headers.authorization || '' } }, incoming => {
      response.writeHead(incoming.statusCode || 502, { 'content-type': 'application/json', 'cache-control': 'no-store' });
      incoming.pipe(response);
    });
    upstream.on('timeout', () => upstream.destroy());
    upstream.on('error', () => { if (!response.headersSent) response.writeHead(502); response.end(); });
    upstream.end(body);
  });
}).listen(7151, '127.0.0.1', () => console.log('Sandbox webhook proxy listening on 127.0.0.1:7151'))
  .on('error', error => { console.error(error.code || 'proxy_error'); process.exitCode = 1; });
