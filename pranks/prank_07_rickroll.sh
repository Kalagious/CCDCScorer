#!/bin/bash
# prank_07_rickroll.sh — Replaces /var/www/html/index.php with a rick roll page
# Original is backed up to index.php.bak
# Undo: bash prank_07_rickroll.sh --remove

if [ "$1" = "--remove" ]; then
  if [ -f /var/www/html/index.php.bak ]; then
    mv /var/www/html/index.php.bak /var/www/html/index.php
    echo "[+] index.php restored from backup"
  else
    echo "[!] No backup found at index.php.bak — cannot restore"
    exit 1
  fi
  exit 0
fi

# Back up the real index.php
cp /var/www/html/index.php /var/www/html/index.php.bak
echo "[+] Original index.php backed up to index.php.bak"

cat > /var/www/html/index.php <<'EOF'
<!DOCTYPE html>
<html>
<head>
  <title>CRITICAL SECURITY INCIDENT - IR Portal</title>
  <style>
    body{background:#000;color:#39ff14;font-family:monospace;text-align:center;padding-top:80px}
    h1{font-size:36px;animation:blink 1s step-end infinite}
    @keyframes blink{0%,100%{opacity:1}50%{opacity:0}}
    p{font-size:16px;color:#ff2d78}
    .counter{font-size:48px;color:#ffd700;margin:20px}
  </style>
  <meta http-equiv="refresh" content="5;url=https://www.youtube.com/watch?v=dQw4w9WgXcQ">
</head>
<body>
  <h1>⚠ CRITICAL SECURITY INCIDENT DETECTED ⚠</h1>
  <p>Your credentials have been compromised.</p>
  <p>Connecting to Incident Response Portal...</p>
  <div class="counter" id="c">5</div>
  <p style="color:#555;font-size:12px">(you got got)</p>
  <script>
    let n = 5;
    setInterval(() => { document.getElementById('c').textContent = --n; }, 1000);
  </script>
</body>
</html>
EOF

chown www-data:www-data /var/www/html/index.php
echo "[+] Rick roll installed at http://TARGET/ — original saved to index.php.bak"
echo "    This will also break the HTTP uptime check — use --remove to restore before scoring"

