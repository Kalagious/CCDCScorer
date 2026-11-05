# Deploying the CCDC Scoring App on Ubuntu

This covers the scoring web app itself (this repo), not the vulnerable target
boxes — those are provisioned separately with `setup-vm.sh` / `vm-setup-guide.txt`.

## 1. Requirements

- Ubuntu 22.04 or 24.04 (fresh VM or bare metal).
- **Node.js 22.5+ (Node 24 LTS recommended).** This app uses the built-in
  `node:sqlite` module, which does not exist before Node 22.5 and is still
  experimental pre-24. Ubuntu's default `apt` package (`nodejs` on 22.04/24.04)
  is too old — you must install from NodeSource or nvm.
- No C/C++ build toolchain is required. All dependencies (`bcryptjs`, `express`,
  `express-session`, `ws`) are pure JS. `ssh2` has an optional native addon for
  faster crypto; if it fails to build it silently falls back to a pure-JS
  implementation, so `build-essential` is not a hard requirement.

## 2. Install Node.js 24 via NodeSource

```bash
curl -fsSL https://deb.nodesource.com/setup_24.x | sudo -E bash -
sudo apt-get install -y nodejs
node -v   # should print v24.x.x
```

(Alternative: `nvm install 24 && nvm use 24` if you'd rather not touch system Node.)

## 3. Get the project onto the box

From your dev machine:

```bash
scp -r CCDCPractice/ user@TARGET:/opt/ccdc-scoring
```

Or, if the repo is in git, `git clone` it directly on the box into `/opt/ccdc-scoring`.
Exclude `node_modules/`, `ccdc.db`, `ccdc.db-wal`, `ccdc.db-shm` when copying — let
the target install its own `node_modules` and start with a fresh database.

## 4. Install dependencies and do a first run

```bash
cd /opt/ccdc-scoring
npm install
node server.js
```

You should see `CCDC Scoring running on http://localhost:3000`. `db.js` creates
`ccdc.db` and seeds the admin account (`admin` / `P@ssword!`) on first run.
Ctrl+C to stop, then move to running it as a service.

**Change the default password and session secret before the competition:**
- Admin password: set in `db.js` (currently hardcoded to `P@ssword!`) — log in
  once the service is running and change it there if you add a password-change
  flow, or edit the hash in `db.js` and restart.
- Session secret: `server.js` line ~19, `secret: 'ccdc-secret-key-change-me'`.
  Change this to a random string before exposing the app beyond localhost.

## 5. Run it persistently with systemd

Create `/etc/systemd/system/ccdc-scoring.service`:

```ini
[Unit]
Description=CCDC Scoring App
After=network.target

[Service]
Type=simple
User=ccdc
WorkingDirectory=/opt/ccdc-scoring
ExecStart=/usr/bin/node server.js
Restart=always
RestartSec=3

[Install]
WantedBy=multi-user.target
```

Create a dedicated user and hand it ownership first, since running as root is
unnecessary:

```bash
sudo useradd -r -s /usr/sbin/nologin -d /opt/ccdc-scoring ccdc
sudo chown -R ccdc:ccdc /opt/ccdc-scoring
```

Then enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now ccdc-scoring
sudo systemctl status ccdc-scoring
```

Logs: `journalctl -u ccdc-scoring -f`

(If you'd rather not touch systemd, `pm2 start server.js --name ccdc-scoring`
works too — just run `pm2 startup` and `pm2 save` so it survives reboots.)

## 6. Networking

- The app listens on **port 3000**, hardcoded in `server.js` (`server.listen(3000, ...)`).
  Change that line if you need a different port.
- Open 3000 to whoever needs to view the scoreboard:
  ```bash
  sudo ufw allow 3000/tcp
  ```
- The scoring engine reaches out *from* this box to each team's target machines
  on whatever ports their profiles specify (22 for SSH checks, arbitrary TCP
  ports, 80/443 for HTTP checks, plus ICMP for ping checks). Make sure this
  box's outbound traffic isn't blocked by a firewall/security group between it
  and the competition network — inbound rules on the *target* machines matter
  more here than this box's own firewall.
- If this box sits behind a reverse proxy (nginx) for TLS, proxy_pass to
  `localhost:3000` and make sure WebSocket upgrade headers are passed through
  (the live scoreboard updates depend on the `ws` connection, not just HTTP):
  ```nginx
  location / {
    proxy_pass http://localhost:3000;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
  }
  ```

## 7. Data persistence

Everything lives in `ccdc.db` (plus `-wal`/`-shm` sidecar files) in the project
root — no external database needed. Back it up before the event:

```bash
sqlite3 /opt/ccdc-scoring/ccdc.db ".backup /opt/ccdc-scoring/ccdc.db.bak"
```

Exported profiles land in `profiles/` as JSON — worth backing up separately so
you can re-import them onto a clean `ccdc.db` if needed.

## 8. Quick smoke test

```bash
curl -s -X POST http://localhost:3000/api/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"P@ssword!"}'
```

Should return `{"ok":true}`. Then browse to `http://TARGET:3000/admin.html` to
log in and start building profiles, and `http://TARGET:3000/` for the player
scoreboard.
