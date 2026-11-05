#!/bin/bash
# prank_06_phantom_process.sh — Installs a spooky-named service that shows in ps and systemctl
# Undo: bash prank_06_phantom_process.sh --remove

if [ "$1" = "--remove" ]; then
  systemctl disable --now definitely_not_a_backdoor 2>/dev/null || true
  rm -f /etc/systemd/system/definitely_not_a_backdoor.service
  rm -f /usr/local/bin/definitely_not_a_backdoor
  systemctl daemon-reload
  echo "[+] Phantom process removed"
  exit 0
fi

cat > /usr/local/bin/definitely_not_a_backdoor <<'EOF'
#!/bin/bash
while true; do sleep 3600; done
EOF
chmod +x /usr/local/bin/definitely_not_a_backdoor

cat > /etc/systemd/system/definitely_not_a_backdoor.service <<'EOF'
[Unit]
Description=Totally Legitimate System Service

[Service]
ExecStart=/usr/local/bin/definitely_not_a_backdoor
Restart=always
User=root

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now definitely_not_a_backdoor

echo "[+] Phantom process installed — visible in 'ps aux' and 'systemctl list-units'"
echo "    Blue team will spend ages googling 'definitely_not_a_backdoor'"
