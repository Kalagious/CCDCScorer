const { DatabaseSync } = require('node:sqlite');
const path = require('path');
const bcrypt = require('bcryptjs');

const db = new DatabaseSync(path.join(__dirname, 'ccdc.db'));

// Set pragmas individually — multi-statement exec doesn't guarantee order
db.exec('PRAGMA journal_mode=WAL');
db.exec('PRAGMA foreign_keys=OFF');

db.exec(`
CREATE TABLE IF NOT EXISTS admins (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  username TEXT UNIQUE NOT NULL,
  password_hash TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS teams (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT UNIQUE NOT NULL,
  color TEXT NOT NULL DEFAULT '#4f8ef7'
);

CREATE TABLE IF NOT EXISTS profiles (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  description TEXT,
  created_at INTEGER DEFAULT (unixepoch())
);

CREATE TABLE IF NOT EXISTS instances (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  profile_id INTEGER NOT NULL,
  team_id INTEGER,
  ip TEXT NOT NULL,
  label TEXT
);

CREATE TABLE IF NOT EXISTS score_events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  instance_id INTEGER NOT NULL,
  application_id INTEGER NOT NULL,
  team_id INTEGER,
  success INTEGER NOT NULL DEFAULT 0,
  points_awarded INTEGER NOT NULL DEFAULT 0,
  checked_at INTEGER DEFAULT (unixepoch())
);
`);

// Migrate applications table — rebuild if CHECK constraint is missing 'http' or columns are missing
const appMeta = db.prepare("SELECT sql FROM sqlite_master WHERE type='table' AND name='applications'").get();
if (!appMeta) {
  db.exec(`
    CREATE TABLE applications (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      profile_id INTEGER NOT NULL,
      name TEXT NOT NULL,
      type TEXT NOT NULL,
      port INTEGER,
      check_string TEXT,
      ssh_user TEXT,
      ssh_password TEXT,
      http_path TEXT,
      penalty_points INTEGER NOT NULL DEFAULT 0,
      points INTEGER NOT NULL DEFAULT 100,
      interval_seconds INTEGER NOT NULL DEFAULT 60
    )
  `);
} else {
  const needsRebuild = !appMeta.sql.includes("'http'");
  const cols = db.prepare('PRAGMA table_info(applications)').all().map(c => c.name);

  if (needsRebuild) {
    // Rebuild without the restrictive CHECK constraint, preserving existing rows
    db.exec(`
      CREATE TABLE applications_new (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        profile_id INTEGER NOT NULL,
        name TEXT NOT NULL,
        type TEXT NOT NULL,
        port INTEGER,
        check_string TEXT,
        ssh_user TEXT,
        ssh_password TEXT,
        http_path TEXT,
        penalty_points INTEGER NOT NULL DEFAULT 0,
        points INTEGER NOT NULL DEFAULT 100,
        interval_seconds INTEGER NOT NULL DEFAULT 60
      );
      INSERT INTO applications_new (id, profile_id, name, type, port, check_string, ssh_user, ssh_password,
        http_path, points, interval_seconds)
        SELECT id, profile_id, name, type, port, check_string, ssh_user, ssh_password,
          ${cols.includes('http_path') ? 'http_path' : 'NULL'}, points, interval_seconds
        FROM applications;
      DROP TABLE applications;
      ALTER TABLE applications_new RENAME TO applications;
    `);
  } else {
    if (!cols.includes('http_path'))      db.exec('ALTER TABLE applications ADD COLUMN http_path TEXT');
    if (!cols.includes('penalty_points')) db.exec('ALTER TABLE applications ADD COLUMN penalty_points INTEGER NOT NULL DEFAULT 0');
  }
}

const adminCount = db.prepare('SELECT COUNT(*) as c FROM admins').get();
const adminHash = bcrypt.hashSync('P@ssword!', 10);
if (adminCount.c === 0) {
  db.prepare('INSERT INTO admins (username, password_hash) VALUES (?,?)').run('admin', adminHash);
} else {
  db.prepare('UPDATE admins SET password_hash=? WHERE username=?').run(adminHash, 'admin');
}

module.exports = db;
