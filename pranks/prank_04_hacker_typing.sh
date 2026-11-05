#!/bin/bash
# prank_04_hacker_typing.sh — Fake "exfiltration in progress" message on every login
# Undo: bash prank_04_hacker_typing.sh --remove

if [ "$1" = "--remove" ]; then
  rm -f /etc/profile.d/hax.sh
  echo "[+] Hacker typing effect removed"
  exit 0
fi

cat > /etc/profile.d/hax.sh <<'EOF'
#!/bin/bash
if [ -t 1 ]; then
  MSG="Initializing root backdoor... done. Exfiltrating /etc/shadow... done. Have a nice day :)"
  for ((i=0; i<${#MSG}; i++)); do
    printf '%s' "${MSG:$i:1}"
    sleep 0.03
  done
  echo ""
fi
EOF
chmod +x /etc/profile.d/hax.sh

echo "[+] Hacker typing effect installed — runs on every interactive login"
