// Stands in for the spray device on the PC. Usage: node pretend-device.js <worker url> <DEVICE_KEY>
// The real device (for example an ESP32) must follow the same rules.
const [url = 'http://127.0.0.1:8787', key] = process.argv.slice(2);
if (!key) throw new Error('give the DEVICE_KEY');

const MAX_MS = 1500;     // never spray longer than this, whatever the message says
const MAX_AGE_MS = 3000; // ignore late messages
const GAP_MS = 3000;     // minimum time between sprays
let last = 0;

const pump = (on) => console.log(on ? 'FAN + WATER ON' : 'off'); // real device: switch the relay here, off at boot

function connect() {
  const ws = new WebSocket(url.replace(/^http/, 'ws').replace(/\/$/, '') + '/live?key=' + key);
  ws.onopen = () => console.log('connected, waiting for splashes');
  ws.onmessage = ({ data }) => {
    let m;
    try { m = JSON.parse(data); } catch { return; }
    const now = Date.now();
    if (m.type !== 'splash' || Math.abs(now - m.t) > MAX_AGE_MS || now - last < GAP_MS) return;
    last = now;
    pump(true);
    setTimeout(() => pump(false), Math.min(MAX_MS, Math.max(0, m.ms || 0)));
  };
  ws.onclose = () => { pump(false); setTimeout(connect, 2000); };
  ws.onerror = () => ws.close();
}
connect();
