# Cloud (Cloudflare)

**Not deployed.** Deploy only after the doctor / supervisor approves storing patient data online.

## Test on this PC (nothing goes online)
Create `.dev.vars` (never commit it) with two test keys:

    THERAPIST_KEY=...
    DEVICE_KEY=...

Then, in this folder:

    wrangler d1 execute knee-rehab --local --file schema.sql
    wrangler dev

## Go online (after approval)
    wrangler d1 create knee-rehab          # put the printed id in wrangler.toml
    wrangler d1 execute knee-rehab --remote --file schema.sql
    wrangler secret put THERAPIST_KEY      # long random value
    wrangler secret put DEVICE_KEY         # a different one
    wrangler deploy

## Connect Unity
Create `cloud.json` next to `patients.json` (AppData/LocalLow/DefaultCompany/GP2):

    { "url": "https://knee-rehab.<account>.workers.dev", "key": "<THERAPIST_KEY>" }

Without this file Unity sends nothing.

## Spray device
Test without hardware: `node pretend-device.js http://127.0.0.1:8787 <DEVICE_KEY>`.
Unity sends a splash only when the patient has WATER SPRAY ON (Edit profile; off by default).

Rules for the real device: only on a fully healed wound with clinician approval; off at boot and on
disconnect; maximum spray time and gap enforced on the device; ignore old messages; a physical off switch;
low voltage, electronics sealed away from the water; watch for a wet floor. Needs a clock (NTP) to judge
message age. Have someone who knows electronics check it before it goes near a person.

## Endpoints
| Request | Who | Use |
|---|---|---|
| `POST /sync` `{patient, sessions}` | PC | Save results (re-sending is safe) |
| `GET /patients/<id>/sessions` | PC | Read results |
| `POST /live` `{...}` | PC | Live angle, or `{"type":"splash","ms":1000,"t":...}` |
| `GET /live` (WebSocket, `?key=`) | device, PC | Listen |

Left to decide: a real login per therapist (now one shared key), and a key per clinic or device.
