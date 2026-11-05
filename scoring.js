const net = require('net');
const http = require('http');
const https = require('https');
const { exec } = require('child_process');
const { Client } = require('ssh2');
const db = require('./db');

let timers = {};
let broadcast = () => {};

function setBroadcast(fn) {
  broadcast = fn;
}

function checkPing(ip) {
  return new Promise(resolve => {
    const cmd = process.platform === 'win32'
      ? `ping -n 1 -w 1000 ${ip}`
      : `ping -c 1 -W 1 ${ip}`;
    exec(cmd, (err, stdout) => {
      resolve(!err && /ttl=/i.test(stdout));
    });
  });
}

function checkTcp(ip, port, checkString, timeoutMs = 5000) {
  return new Promise(resolve => {
    const sock = new net.Socket();
    let data = '';
    sock.setTimeout(timeoutMs);
    sock.connect(port, ip, () => {});
    sock.on('data', chunk => { data += chunk.toString(); });
    sock.on('timeout', () => { sock.destroy(); resolve(false); });
    sock.on('error', () => resolve(false));
    sock.on('close', () => {
      if (!checkString) resolve(true);
      else resolve(data.includes(checkString));
    });
    // also resolve on connect if no checkString and nothing expected back
    sock.on('connect', () => {
      if (!checkString) {
        sock.destroy();
        resolve(true);
      }
    });
  });
}

function checkSsh(ip, port, user, password, timeoutMs = 8000) {
  return new Promise(resolve => {
    const conn = new Client();
    const timer = setTimeout(() => { conn.end(); resolve(false); }, timeoutMs);
    conn.on('ready', () => {
      clearTimeout(timer);
      conn.end();
      resolve(true);
    });
    conn.on('error', () => { clearTimeout(timer); resolve(false); });
    conn.connect({ host: ip, port: port || 22, username: user, password });
  });
}

function checkHttp(ip, port, httpPath, checkString, timeoutMs = 8000) {
  return new Promise(resolve => {
    const path = httpPath || '/';
    const useHttps = port === 443;
    const transport = useHttps ? https : http;
    const portNum = port || 80;
    const opts = {
      hostname: ip,
      port: portNum,
      path,
      method: 'GET',
      timeout: timeoutMs,
      // don't reject self-signed certs in a lab environment
      rejectUnauthorized: false,
    };
    const req = transport.request(opts, res => {
      let body = '';
      res.on('data', chunk => { body += chunk.toString(); });
      res.on('end', () => {
        if (!checkString) resolve(true);
        else resolve(body.includes(checkString));
      });
    });
    req.on('timeout', () => { req.destroy(); resolve(false); });
    req.on('error', () => resolve(false));
    req.end();
  });
}

async function runCheck(instance, app) {
  let success = false;
  try {
    if (app.type === 'ping') {
      success = await checkPing(instance.ip);
    } else if (app.type === 'tcp') {
      success = await checkTcp(instance.ip, app.port, app.check_string);
    } else if (app.type === 'ssh') {
      success = await checkSsh(instance.ip, app.port, app.ssh_user, app.ssh_password);
    } else if (app.type === 'http') {
      success = await checkHttp(instance.ip, app.port, app.http_path, app.check_string);
    }
  } catch (_) {
    success = false;
  }

  const points = success ? app.points : -(app.penalty_points || 0);
  try {
    db.prepare(
      'INSERT INTO score_events (instance_id, application_id, team_id, success, points_awarded) VALUES (?,?,?,?,?)'
    ).run(instance.id, app.id, instance.team_id, success ? 1 : 0, points);
  } catch (_) {
    // stale app/instance ID (e.g. profile was edited) — skip silently
    return;
  }

  broadcast({ type: 'check', instanceId: instance.id, appId: app.id, success, points, teamId: instance.team_id });
}

function scheduleApp(instance, app) {
  const key = `${instance.id}-${app.id}`;
  if (timers[key]) return;
  const tick = () => runCheck(instance, app);
  tick();
  timers[key] = setInterval(tick, app.interval_seconds * 1000);
}

function unscheduleApp(instanceId, appId) {
  const key = `${instanceId}-${appId}`;
  if (timers[key]) { clearInterval(timers[key]); delete timers[key]; }
}

// Call after a profile's applications are replaced so timers use fresh app IDs
function rescheduleProfile(profileId) {
  // Clear any timers whose key belongs to an instance of this profile
  const instances = db.prepare('SELECT * FROM instances WHERE profile_id = ?').all(profileId);
  for (const inst of instances) {
    for (const key of Object.keys(timers)) {
      if (key.startsWith(`${inst.id}-`)) {
        clearInterval(timers[key]);
        delete timers[key];
      }
    }
    const apps = db.prepare('SELECT * FROM applications WHERE profile_id = ?').all(profileId);
    for (const app of apps) scheduleApp(inst, app);
  }
}

function startAll() {
  const instances = db.prepare('SELECT * FROM instances').all();
  for (const inst of instances) {
    const apps = db.prepare('SELECT * FROM applications WHERE profile_id = ?').all(inst.profile_id);
    for (const app of apps) scheduleApp(inst, app);
  }
}

function stopAll() {
  for (const key of Object.keys(timers)) { clearInterval(timers[key]); delete timers[key]; }
}

function isRunning() {
  return Object.keys(timers).length > 0;
}

module.exports = { startAll, stopAll, isRunning, scheduleApp, unscheduleApp, rescheduleProfile, setBroadcast };
