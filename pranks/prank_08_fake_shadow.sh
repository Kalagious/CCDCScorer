#!/bin/bash
# prank_08_fake_shadow.sh — Adds a fake alarming-looking comment to /etc/shadow
# Harmless — it is just a comment line, not a real account entry
# Undo: bash prank_08_fake_shadow.sh --remove

if [ "$1" = "--remove" ]; then
  sed -i '/redteam_backdoor/d' /etc/shadow
  echo "[+] Fake shadow entry removed"
  exit 0
fi

echo "# redteam_backdoor:$(openssl rand -hex 16):0:0:99999:7:::" >> /etc/shadow

echo "[+] Fake shadow entry added — looks scary in 'cat /etc/shadow' output"
echo "    It is a comment line and does nothing, but blue team will freak out"
