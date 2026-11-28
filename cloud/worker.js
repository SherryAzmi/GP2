// Knee Rehab cloud: saved results (D1) and live messages (Durable Object WebSocket).
// Two secrets, never committed: THERAPIST_KEY (the clinic PC) and DEVICE_KEY (the spray device, listen only).

const json = (data, status = 200) => Response.json(data, { status });

const PATIENT = ['id', 'operated_leg', 'age', 'height_cm', 'weight_kg', 'surgery_type', 'surgery_date',
  'safety_limit', 'comfortable_max', 'extension_goal', 'updated'];
const SESSION = ['patient_id', 'date', 'game', 'stage', 'leg', 'duration_s', 'done', 'planned', 'stopped_early', 'eased',
  'success_percent', 'score', 'best_bend', 'avg_bend', 'straightest', 'safety_alerts', 'balance_pauses', 'pain', 'settings'];

// Only the listed columns are stored, so anything extra (a name, a phone) is dropped.
function upsert(env, table, columns, row) {
  return env.DB.prepare(
    `INSERT OR REPLACE INTO ${table} (${columns.join(',')}) VALUES (${columns.map(() => '?').join(',')})`
  ).bind(...columns.map((c) => row[c] ?? null));
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const key = (request.headers.get('Authorization') || '').replace('Bearer ', '') || url.searchParams.get('key');
    const therapist = !!env.THERAPIST_KEY && key === env.THERAPIST_KEY;
    const device = !!env.DEVICE_KEY && key === env.DEVICE_KEY;
    if (!therapist && !device) return json({ error: 'unauthorized' }, 401);

    const room = () => env.LIVE.get(env.LIVE.idFromName('clinic'));

    // Device (or therapist) listens for live messages.
    if (url.pathname === '/live' && request.headers.get('Upgrade') === 'websocket') return room().fetch(request);
    if (!therapist) return json({ error: 'forbidden' }, 403);

    if (url.pathname === '/live' && request.method === 'POST') return room().fetch(request);

    if (url.pathname === '/sync' && request.method === 'POST') {
      const body = await request.json().catch(() => null);
      if (!body?.patient?.id) return json({ error: 'patient.id required' }, 400);
      const sessions = (body.sessions || []).filter((s) => s.date).slice(0, 200);
      await env.DB.batch([
        upsert(env, 'patients', PATIENT, body.patient),
        ...sessions.map((s) => upsert(env, 'sessions', SESSION, { ...s, patient_id: body.patient.id })),
      ]);
      return json({ saved: sessions.length });
    }

    const match = url.pathname.match(/^\/patients\/([\w-]+)\/sessions$/);
    if (match && request.method === 'GET') {
      const { results } = await env.DB.prepare('SELECT * FROM sessions WHERE patient_id = ? ORDER BY date')
        .bind(match[1]).all();
      return json(results);
    }
    return json({ error: 'not found' }, 404);
  },
};

// Passes each posted message to everyone listening. Nothing is stored, so old messages can never be replayed.
export class LiveRoom {
  constructor(state) {
    this.state = state;
  }

  async fetch(request) {
    if (request.headers.get('Upgrade') === 'websocket') {
      const [client, server] = Object.values(new WebSocketPair());
      this.state.acceptWebSocket(server);
      return new Response(null, { status: 101, webSocket: client });
    }
    const message = await request.text();
    if (message.length > 1000) return json({ error: 'too long' }, 413);
    const listeners = this.state.getWebSockets();
    for (const socket of listeners) socket.send(message);
    return json({ listeners: listeners.length });
  }
}
