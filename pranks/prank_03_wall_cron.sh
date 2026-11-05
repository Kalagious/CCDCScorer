#!/bin/bash
# prank_03_wall_cron.sh — Broadcasts a taunt to all terminals every 5 minutes
# Undo: bash prank_03_wall_cron.sh --remove

if [ "$1" = "--remove" ]; then
  rm -f /etc/cron.d/redteam_taunt
  echo "[+] Wall cron removed"
  exit 0
fi

cat > /etc/cron.d/redteam_taunt <<'EOF'
*/5 * * * * root wall "RED TEAM: Still here. How's that patching going? :)"
EOF

echo "[+] Wall cron installed — broadcasts every 5 minutes to all logged-in terminals"
