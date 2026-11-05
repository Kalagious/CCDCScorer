const express = require('express');
const session = require('express-session');
const bcrypt = require('bcryptjs');
const path = require('path');
const fs = require('fs');
const WebSocket = require('ws');
const http = require('http');
const db = require('./db');
const scoring = require('./scoring');

const app = express();
const server = http.createServer(app);
const wss = new WebSocket.Server({ server });

app.use(express.json());
app.use(express.urlencoded({ extended: true }));
app.use(express.static(path.join(__dirname, 'public')));
app.use(session({
  secret: 'ccdc-secret-key-change-me',
  resave: false,
  saveUninitialized: false,
  cookie: { maxAge: 86400000 }
}));

// WebSocket broadcast
const clients = new Set();
wss.on('connection', ws => {
  clients.add(ws);
  ws.on('close', () => clients.delete(ws));
});
scoring.setBroadcast(msg => {
  const data = JSON.stringify(msg);
  for (const c of clients) if (c.readyState === WebSocket.OPEN) c.send(data);
});

function requireAdmin(req, res, next) {
  if (req.session && req.session.adminId) return next();
  res.status(401).json({ error: 'Unauthorized' });
}

// Auth
app.post('/api/login', (req, res) => {
  const { username, password } = req.body;
  const admin = db.prepare('SELECT * FROM admins WHERE username = ?').get(username);
  if (!admin || !bcrypt.compareSync(password, admin.password_hash)) {
    return res.status(401).json({ error: 'Invalid credentials' });
  }
  req.session.adminId = admin.id;
  res.json({ ok: true });
});

app.post('/api/logout', (req, res) => {
  req.session.destroy();
  res.json({ ok: true });
});

app.get('/api/me', (req, res) => {
  res.json({ admin: !!req.session.adminId });
});

// Teams
app.get('/api/teams', (req, res) => {
  res.json(db.prepare('SELECT * FROM teams').all());
});

app.post('/api/teams', requireAdmin, (req, res) => {
  const { name, color } = req.body;
  const r = db.prepare('INSERT INTO teams (name, color) VALUES (?,?)').run(name, color || '#4f8ef7');
  res.json({ id: r.lastInsertRowid });
});

app.put('/api/teams/:id', requireAdmin, (req, res) => {
  const { name, color } = req.body;
  db.prepare('UPDATE teams SET name=?, color=? WHERE id=?').run(name, color, req.params.id);
  res.json({ ok: true });
});

app.delete('/api/teams/:id', requireAdmin, (req, res) => {
  db.prepare('DELETE FROM teams WHERE id=?').run(req.params.id);
  res.json({ ok: true });
});

// Profiles
app.get('/api/profiles', requireAdmin, (req, res) => {
  const profiles = db.prepare('SELECT * FROM profiles').all();
  for (const p of profiles) {
    p.applications = db.prepare('SELECT * FROM applications WHERE profile_id=?').all(p.id);
  }
  res.json(profiles);
});

app.post('/api/profiles', requireAdmin, (req, res) => {
  const { name, description, applications } = req.body;
  const r = db.prepare('INSERT INTO profiles (name, description) VALUES (?,?)').run(name, description || '');
  const pid = r.lastInsertRowid;
  if (Array.isArray(applications)) {
    const insertApp = db.prepare(
      'INSERT INTO applications (profile_id, name, type, port, check_string, ssh_user, ssh_password, http_path, penalty_points, points, interval_seconds) VALUES (?,?,?,?,?,?,?,?,?,?,?)'
    );
    for (const a of applications) {
      insertApp.run(pid, a.name, a.type, a.port || null, a.check_string || null, a.ssh_user || null, a.ssh_password || null, a.http_path || null, a.penalty_points ?? 0, a.points ?? 100, a.interval_seconds ?? 60);
    }
  }
  res.json({ id: pid });
});

app.put('/api/profiles/:id', requireAdmin, (req, res) => {
  const { name, description, applications } = req.body;
  const pid = req.params.id;
  db.prepare('UPDATE profiles SET name=?, description=? WHERE id=?').run(name, description || '', pid);
  if (Array.isArray(applications)) {
    db.prepare('DELETE FROM applications WHERE profile_id=?').run(pid);
    const insertApp = db.prepare(
      'INSERT INTO applications (profile_id, name, type, port, check_string, ssh_user, ssh_password, http_path, penalty_points, points, interval_seconds) VALUES (?,?,?,?,?,?,?,?,?,?,?)'
    );
    for (const a of applications) {
      insertApp.run(pid, a.name, a.type, a.port || null, a.check_string || null, a.ssh_user || null, a.ssh_password || null, a.http_path || null, a.penalty_points ?? 0, a.points ?? 100, a.interval_seconds ?? 60);
    }
    if (scoring.isRunning()) scoring.rescheduleProfile(pid);
  }
  res.json({ ok: true });
});

app.delete('/api/profiles/:id', requireAdmin, (req, res) => {
  db.prepare('DELETE FROM profiles WHERE id=?').run(req.params.id);
  res.json({ ok: true });
});

// Profile save/restore
app.get('/api/profiles/:id/export', requireAdmin, (req, res) => {
  const profile = db.prepare('SELECT * FROM profiles WHERE id=?').get(req.params.id);
  if (!profile) return res.status(404).json({ error: 'Not found' });
  profile.applications = db.prepare('SELECT * FROM applications WHERE profile_id=?').all(profile.id);
  res.setHeader('Content-Disposition', `attachment; filename="profile-${profile.name}.json"`);
  res.json(profile);
});

app.post('/api/profiles/import', requireAdmin, express.json(), (req, res) => {
  const { name, description, applications } = req.body;
  const r = db.prepare('INSERT INTO profiles (name, description) VALUES (?,?)').run(name, description || '');
  const pid = r.lastInsertRowid;
  const insertApp = db.prepare(
    'INSERT INTO applications (profile_id, name, type, port, check_string, ssh_user, ssh_password, points, interval_seconds) VALUES (?,?,?,?,?,?,?,?,?)'
  );
  for (const a of (applications || [])) {
    insertApp.run(pid, a.name, a.type, a.port || null, a.check_string || null, a.ssh_user || null, a.ssh_password || null, a.points ?? 100, a.interval_seconds ?? 60);
  }
  res.json({ id: pid });
});

// Instances
app.get('/api/instances', requireAdmin, (req, res) => {
  res.json(db.prepare('SELECT i.*, p.name as profile_name, t.name as team_name FROM instances i LEFT JOIN profiles p ON i.profile_id=p.id LEFT JOIN teams t ON i.team_id=t.id').all());
});

app.post('/api/instances', requireAdmin, (req, res) => {
  const { profile_id, team_id, ip, label } = req.body;
  const r = db.prepare('INSERT INTO instances (profile_id, team_id, ip, label) VALUES (?,?,?,?)').run(profile_id, team_id || null, ip, label || null);
  if (scoring.isRunning()) {
    const inst = db.prepare('SELECT * FROM instances WHERE id=?').get(r.lastInsertRowid);
    const apps = db.prepare('SELECT * FROM applications WHERE profile_id=?').all(inst.profile_id);
    for (const a of apps) scoring.scheduleApp(inst, a);
  }
  res.json({ id: r.lastInsertRowid });
});

app.put('/api/instances/:id', requireAdmin, (req, res) => {
  const { team_id, ip, label } = req.body;
  db.prepare('UPDATE instances SET team_id=?, ip=?, label=? WHERE id=?').run(team_id || null, ip, label || null, req.params.id);
  res.json({ ok: true });
});

app.delete('/api/instances/:id', requireAdmin, (req, res) => {
  const id = req.params.id;
  const inst = db.prepare('SELECT * FROM instances WHERE id=?').get(id);
  if (inst) {
    const apps = db.prepare('SELECT * FROM applications WHERE profile_id=?').all(inst.profile_id);
    for (const a of apps) scoring.unscheduleApp(id, a.id);
  }
  db.prepare('DELETE FROM instances WHERE id=?').run(id);
  res.json({ ok: true });
});

// Scoring control
app.post('/api/scoring/start', requireAdmin, (req, res) => {
  scoring.startAll();
  res.json({ ok: true });
});

app.post('/api/scoring/stop', requireAdmin, (req, res) => {
  scoring.stopAll();
  res.json({ ok: true });
});

app.get('/api/scoring/status', requireAdmin, (req, res) => {
  res.json({ running: scoring.isRunning() });
});

// Score data for player view
app.get('/api/scores', (req, res) => {
  const teams = db.prepare('SELECT * FROM teams').all();
  const result = teams.map(team => {
    const total = db.prepare(
      'SELECT COALESCE(SUM(points_awarded),0) as pts FROM score_events WHERE team_id=?'
    ).get(team.id).pts;

    const instances = db.prepare('SELECT * FROM instances WHERE team_id=?').all(team.id);
    const services = instances.flatMap(inst => {
      const apps = db.prepare('SELECT * FROM applications WHERE profile_id=?').all(inst.profile_id);
      return apps.map(app => {
        const latest = db.prepare(
          'SELECT * FROM score_events WHERE instance_id=? AND application_id=? ORDER BY checked_at DESC LIMIT 1'
        ).get(inst.id, app.id);
        return {
          instanceId: inst.id,
          instanceLabel: inst.label || inst.ip,
          ip: inst.ip,
          appId: app.id,
          appName: app.name,
          appType: app.type,
          up: latest ? !!latest.success : null,
          lastChecked: latest ? latest.checked_at : null
        };
      });
    });

    const buckets = db.prepare(
      `SELECT (checked_at / 300) * 300 as bucket, SUM(points_awarded) as pts
       FROM score_events WHERE team_id=?
       GROUP BY bucket ORDER BY bucket ASC LIMIT 60`
    ).all(team.id);
    // Convert to cumulative running total
    let running = 0;
    const history = buckets.map(b => { running += b.pts; return { bucket: b.bucket, pts: running }; });

    return { ...team, total, services, history };
  });
  res.json(result);
});

// Score history for a specific team
app.get('/api/scores/team/:id', (req, res) => {
  const team = db.prepare('SELECT * FROM teams WHERE id=?').get(req.params.id);
  if (!team) return res.status(404).json({ error: 'Not found' });

  const instances = db.prepare('SELECT * FROM instances WHERE team_id=?').all(team.id);
  const detail = instances.map(inst => {
    const apps = db.prepare('SELECT * FROM applications WHERE profile_id=?').all(inst.profile_id);
    return {
      ...inst,
      apps: apps.map(app => {
        const events = db.prepare(
          'SELECT * FROM score_events WHERE instance_id=? AND application_id=? ORDER BY checked_at DESC LIMIT 100'
        ).all(inst.id, app.id);
        const upCount = events.filter(e => e.success).length;
        return { ...app, events, uptime: events.length ? (upCount / events.length * 100).toFixed(1) : null };
      })
    };
  });
  res.json({ team, instances: detail });
});

// Reset scores
app.post('/api/scores/reset', requireAdmin, (req, res) => {
  db.prepare('DELETE FROM score_events').run();
  res.json({ ok: true });
});

server.listen(3000, () => console.log('CCDC Scoring running on http://localhost:3000'));
