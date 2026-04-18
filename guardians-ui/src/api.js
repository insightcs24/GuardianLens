// src/api.js
//
// Architecture with YARP on Windows EC2:
//   Browser → YARP proxy (port 80) → /api/* → .NET API (port 5000, localhost)
//                                   → /*    → React static files (served by YARP)
//
// Development:  /api → Vite dev proxy → localhost:5000
// Production (Option A - Vercel):
//   VITE_API_URL = http://YOUR-EC2-IP  (YARP handles routing, no /api prefix needed)
// Production (Option B - YARP serves React):
//   VITE_API_URL = ""  (empty — same origin, YARP routes internally)
// ngrok (HTTPS at edge → http://127.0.0.1:80): keep VITE_API_URL empty; build uses .env.production.

const BASE = (import.meta.env.VITE_API_URL || '') + '/api';

async function request(method, path, body) {
  const opts = { method, headers: { 'Content-Type': 'application/json' } };
  if (body !== undefined) opts.body = JSON.stringify(body);
  const res = await fetch(BASE + path, opts);
  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(err?.error || `HTTP ${res.status}: ${res.statusText}`);
  }
  if (res.status === 204) return null;
  return res.json();
}

const get  = (path)       => request('GET',  path);
const post = (path, body) => request('POST', path, body);

export const api = {
  getDashboard:        ()          => get('/assets/dashboard'),
  getAssets:           ()          => get('/assets'),
  getAsset:            (id)        => get(`/assets/${id}`),
  registerAsset:       (payload)   => post('/assets', payload),
  getViolations:       (status)    => get('/violations' + (status ? `?status=${status}` : '')),
  sendTakedown:        (id)        => post(`/violations/${id}/takedown`),
  dismissViolation:    (id)        => post(`/violations/${id}/dismiss`),
  verifyImage:         (base64)    => post('/violations/verify', base64),
  getAllScans:          ()          => get('/scans'),
  startScan:           (assetId)   => post(`/scans/${assetId}`),
  getAlerts:           (n = 50)    => get(`/alerts?count=${n}`),
  runVisionSearch:     (assetId)   => post(`/vision/${assetId}/search`),
  getBlockchainStatus: ()          => get('/blockchain/status'),
  mintAsset:           (assetId)   => post(`/blockchain/mint/${assetId}`),
  verifyOnChain:       (assetId)   => get(`/blockchain/verify/${assetId}`),
};
