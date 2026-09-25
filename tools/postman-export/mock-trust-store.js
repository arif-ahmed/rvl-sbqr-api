// Minimal mock TrustStore for local development.
//
// Serves GET /trust-store/institutions with an empty institutions list so the
// API's trust sync succeeds at boot. Shared by:
//   - tools/postman-export/Generate-Postman-Export.ps1 (and its .sh twin),
//     which start it as part of the Postman export flow;
//   - manual standalone runs (any dev loop needing a trust store on a fixed
//     port):
//       node tools/postman-export/mock-trust-store.js [port]
// (The Aspire AppHost loop uses the richer in-repo BB.TrustStoreMock project
// instead -- this file stays for the export flow.)
//
// Not suitable for anything beyond local dev.
const http = require('http');
const port = Number(process.argv[2] || 5002);
const server = http.createServer((req, res) => {
  if (req.url === '/trust-store/institutions' && req.method === 'GET') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ institutions: [] }));
  } else {
    res.writeHead(404, {});
    res.end('Not Found');
  }
});
server.listen(port, '127.0.0.1', () => {
  console.log(`Mock TrustStore listening on port ${port}`);
});
process.on('SIGTERM', () => server.close());
